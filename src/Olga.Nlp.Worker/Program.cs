using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Olga.Nlp.Application;
using Olga.Nlp.Infrastructure;
using Pgvector.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration.GetConnectionString("PostgreSql")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSql is required for the worker.");
var connectionBuilder = new NpgsqlConnectionStringBuilder(connection)
{
    Pooling = true,
    MaxPoolSize = 20,
    CommandTimeout = 60,
    SslMode = SslMode.VerifyFull
};
builder.Services.AddSingleton<NpgsqlDataSource>(_ =>
{
    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionBuilder.ConnectionString);
    dataSourceBuilder.UseVector();
    return dataSourceBuilder.Build();
});
builder.Services.AddSingleton<PostgresTransactionGuardInterceptor>();
builder.Services.AddDbContext<NlpDbContext>((services, options) => options
    .AddInterceptors(services.GetRequiredService<PostgresTransactionGuardInterceptor>())
    .UseNpgsql(services.GetRequiredService<NpgsqlDataSource>(),
        postgres => postgres.UseVector().CommandTimeout(60).EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));
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
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (RetryLimitExceededException)
            {
                logger.LogWarning("PostgreSQL worker cycle failed after bounded transient retries");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (NpgsqlException exception) when (exception.IsTransient)
            {
                logger.LogWarning("PostgreSQL worker cycle failed with transient SQL state {SqlState}", (exception as PostgresException)?.SqlState);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NlpDbContext>();
        var provider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
        var repository = scope.ServiceProvider.GetRequiredService<IIntentRepository>();
        var jobs = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var leaseTransaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            // Row locks are transaction-local so PgBouncer transaction pooling cannot leak worker state.
            var leased = await db.ProcessingJobs.FromSqlRaw("""
                SELECT * FROM nlp.nlp_processing_job
                WHERE status IN ('PENDING', 'FAILED')
                  AND available_at <= CURRENT_TIMESTAMP
                  AND (locked_until IS NULL OR locked_until < CURRENT_TIMESTAMP)
                ORDER BY available_at
                LIMIT 25
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(ct);

            var leaseTime = DateTimeOffset.UtcNow;
            foreach (var job in leased)
            {
                job.Status = "RUNNING";
                job.LockedUntil = leaseTime.AddMinutes(2);
                job.UpdatedAt = leaseTime;
            }
            await db.SaveChangesAsync(ct);
            await leaseTransaction.CommitAsync(ct);
            return leased;
        });

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
