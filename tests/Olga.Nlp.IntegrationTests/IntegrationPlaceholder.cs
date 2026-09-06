using Microsoft.EntityFrameworkCore;
using Olga.Nlp.Domain;
using Olga.Nlp.Infrastructure;

namespace Olga.Nlp.IntegrationTests;

public sealed class ProcessingJobIntegrationTests
{
    [Fact]
    public async Task Queued_dispatcher_creates_one_active_embedding_job()
    {
        var options = new DbContextOptionsBuilder<NlpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NlpDbContext(options);
        var now = DateTimeOffset.UtcNow;
        var intent = new Intent("intent", "member", "event", IntentType.Want, "Need storage", "Need storage", now.AddDays(1), IntentStatus.Processing, null, null, "normalizer-v1", NormalizedHash: "hash", CreatedAt: now, UpdatedAt: now);
        var repository = new IntentRepository(db);
        await repository.UpsertProcessingAsync(intent, null, default);
        var dispatcher = new QueuedIntentProcessingDispatcher(db);

        await dispatcher.DispatchAsync(intent, default);
        await dispatcher.DispatchAsync(intent, default);

        var job = await db.ProcessingJobs.SingleAsync();
        Assert.Equal("PENDING", job.Status);
        Assert.Equal("intent", job.IntentId);
    }
}
