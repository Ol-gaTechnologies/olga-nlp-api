using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<EmbeddingRefreshWorker>();
await builder.Build().RunAsync();

public sealed class EmbeddingRefreshWorker(ILogger<EmbeddingRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) { logger.LogDebug("Embedding refresh worker heartbeat"); await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
    }
}
