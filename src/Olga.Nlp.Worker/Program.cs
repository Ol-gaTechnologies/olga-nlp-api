using Microsoft.EntityFrameworkCore;
using Olga.Nlp.Application;
using Olga.Nlp.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration.GetConnectionString("AzureSql")
    ?? throw new InvalidOperationException("ConnectionStrings:AzureSql is required for the worker.");
builder.Services.AddDbContext<NlpDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddSingleton<IEmbeddingProvider, AzureEmbeddingProvider>();
builder.Services.AddScoped<IIntentRepository, IntentRepository>();
builder.Services.AddHostedService<EmbeddingJobWorker>();
await builder.Build().RunAsync();

public sealed class EmbeddingJobWorker(IServiceScopeFactory scopes, ILogger<EmbeddingJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await ProcessBatchAsync(stoppingToken);
            if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NlpDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
        var repository = scope.ServiceProvider.GetRequiredService<IIntentRepository>();
        var now = DateTimeOffset.UtcNow;
        var jobs = await db.ProcessingJobs
            .Where(x => (x.Status == "PENDING" || x.Status == "FAILED") && x.AvailableAt <= now && (x.LockedUntil == null || x.LockedUntil < now))
            .OrderBy(x => x.AvailableAt).Take(25).ToListAsync(ct);

        foreach (var job in jobs)
        {
            job.Status = "RUNNING";
            job.LockedUntil = now.AddMinutes(2);
            job.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        foreach (var job in jobs)
        {
            try
            {
                var intent = await db.Intents.AsNoTracking().SingleOrDefaultAsync(x => x.IntentId == job.IntentId, ct);
                if (intent is null || intent.Status != "PROCESSING")
                {
                    job.Status = "SUCCEEDED";
                    job.ErrorCode = null;
                }
                else
                {
                    var embedding = await provider.EmbedAsync(intent.NormalizedText, ct);
                    await repository.MarkReadyAsync(intent.IntentId, intent.NormalizedText, intent.NormalizedHash, embedding, provider.ModelVersion, intent.PreprocessingVersion, ct);
                    job.Status = "SUCCEEDED";
                    job.ErrorCode = null;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                job.AttemptCount++;
                job.Status = job.AttemptCount >= 5 ? "DEAD" : "FAILED";
                job.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, job.AttemptCount) * 5));
                job.ErrorCode = exception is ArgumentException ? "EMBEDDING_INPUT_INVALID" : "EMBEDDING_PROVIDER_FAILED";
                logger.LogWarning("Embedding job {JobId} failed with {ErrorCode} on attempt {AttemptCount}", job.JobId, job.ErrorCode, job.AttemptCount);
            }
            finally
            {
                job.LockedUntil = null;
                job.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }
        return jobs.Count;
    }
}
