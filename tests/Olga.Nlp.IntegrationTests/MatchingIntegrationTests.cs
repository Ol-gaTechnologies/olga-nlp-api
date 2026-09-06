using Microsoft.EntityFrameworkCore;
using Olga.Nlp.Application;
using Olga.Nlp.Contracts;
using Olga.Nlp.Infrastructure;

namespace Olga.Nlp.IntegrationTests;

public sealed class MatchingIntegrationTests
{
    [Fact]
    public async Task Search_reuses_stored_embeddings_and_excludes_blocked_member()
    {
        var options = new DbContextOptionsBuilder<NlpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NlpDbContext(options);
        var normalizer = new TextNormalizer(new PiiChecker());
        var provider = new FakeEmbeddingProvider();
        var intentRepository = new IntentRepository(db);
        var intentService = new IntentService(intentRepository, normalizer, new InlineIntentProcessingDispatcher(provider, intentRepository));
        var expiry = DateTimeOffset.UtcNow.AddDays(10);
        db.RankingConfigs.Add(new() { RankingVersion = "ranking-test", SemanticWeight = .4, CategoryWeight = .25, IndustryWeight = .15, GeographyWeight = .1, FreshnessWeight = .1, Threshold = .35, ActiveFrom = DateTimeOffset.UtcNow.AddDays(-1) });
        foreach (var member in new[] { "A", "B", "BLOCKED" }) db.MemberEligibility.Add(new() { MemberId = member, ContextId = "event", IsLive = true, IsVisible = true, HasConsent = true });
        db.MemberRelationships.Add(new() { MemberId = "A", OtherMemberId = "BLOCKED", ContextId = "event", IsBlocked = true });
        await db.SaveChangesAsync();
        await Save(intentService, "A", "a-want", "WANT", "cold-chain pharmaceutical storage", "storage", expiry);
        await Save(intentService, "A", "a-offer", "OFFER", "healthcare distribution", "distribution", expiry);
        await Save(intentService, "B", "b-offer", "OFFER", "temperature controlled healthcare warehouse", "storage", expiry);
        await Save(intentService, "B", "b-want", "WANT", "pharmaceutical distributor", "distribution", expiry);
        await Save(intentService, "BLOCKED", "blocked-offer", "OFFER", "cold-chain pharmaceutical storage", "storage", expiry);

        var service = new MatchingService(new CandidateRepository(db), new MatchRequestRepository(db), new MatchResultRepository(db), new RankingConfigRepository(db), normalizer, new ThrowingEmbeddingProvider(), new ReciprocalScorer(), new MatchRanker(new ExplanationGenerator()));
        var result = await service.SearchAsync("A", new MatchSearchRequest("req-1", "a-want", "event", 7), default);
        Assert.Contains(result.Matches, x => x.MemberId == "B");
        Assert.DoesNotContain(result.Matches, x => x.MemberId == "BLOCKED");
        Assert.Equal("fake-embedding-v2", result.ModelVersion);
        Assert.All(result.Matches, x => Assert.True(x.MatchResultId > 0));
        Assert.Equal("COMPLETED", result.Status);
        Assert.Contains(await db.OutboxEvents.ToListAsync(), x => x.EventType == "NlpMatchRequestCompleted.v1" && !x.PayloadJson.Contains("cold-chain", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Requester_lookup_uses_exact_requested_want_and_rejects_offer_id()
    {
        var options = new DbContextOptionsBuilder<NlpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NlpDbContext(options);
        var provider = new FakeEmbeddingProvider();
        var repository = new IntentRepository(db);
        var service = new IntentService(repository, new TextNormalizer(new PiiChecker()), new InlineIntentProcessingDispatcher(provider, repository));
        var expiry = DateTimeOffset.UtcNow.AddDays(10);
        await Save(service, "A", "wanted", "WANT", "cold-chain storage", "storage", expiry);
        await Save(service, "A", "newer", "WANT", "website design", "marketing", expiry);
        await Save(service, "A", "offer", "OFFER", "distribution", "distribution", expiry);

        var candidates = new CandidateRepository(db);
        var selected = await candidates.GetRequesterIntentsAsync("A", "wanted", "event", default);

        Assert.Equal("wanted", selected!.Want!.IntentId);
        Assert.Null(await candidates.GetRequesterIntentsAsync("A", "offer", "event", default));
    }

    [Fact]
    public async Task Match_request_replay_is_idempotent_and_different_input_conflicts()
    {
        var options = new DbContextOptionsBuilder<NlpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NlpDbContext(options);
        var execution = new Olga.Nlp.Domain.MatchExecution("same-key", "hash-1", "A", "want", "event", "en", 7, null, Olga.Nlp.Domain.MatchExecutionStatus.Processing);
        var repository = new MatchRequestRepository(db);

        Assert.Equal(Olga.Nlp.Domain.MatchStartDisposition.Started, (await repository.TryStartAsync(execution, default)).Disposition);
        Assert.Equal(Olga.Nlp.Domain.MatchStartDisposition.InProgress, (await repository.TryStartAsync(execution, default)).Disposition);
        Assert.Equal(Olga.Nlp.Domain.MatchStartDisposition.Conflict, (await repository.TryStartAsync(execution with { RequestHash = "hash-2" }, default)).Disposition);
    }

    [Fact]
    public async Task Feedback_is_append_only_and_corrections_reference_previous_feedback()
    {
        var options = new DbContextOptionsBuilder<NlpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NlpDbContext(options);
        db.MatchResults.Add(new NlpMatchResultRow
        {
            RequestId = "request", RequesterId = "A", CandidateId = "B", Rank = 1,
            SemanticScore = .8, ReciprocalScore = .7, FinalScore = .75, Label = "STRONG_MATCH",
            ReasonCodes = "[]", ReasonText = "Relevant", ModelVersion = "model",
            PreprocessingVersion = "normalizer", RankingVersion = "ranking", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var matchResultId = await db.MatchResults.Select(x => x.MatchResultId).SingleAsync();
        var repository = new FeedbackRepository(db);

        var first = await repository.SaveAsync(matchResultId, "A", "USEFUL", null, null, null, default);
        var correction = await repository.SaveAsync(matchResultId, "A", "NOT_USEFUL", "WRONG_CATEGORY", null, first.FeedbackId, default);

        Assert.Equal(first.FeedbackId, await db.Feedback.Where(x => x.FeedbackId == correction.FeedbackId).Select(x => x.SupersedesFeedbackId).SingleAsync());
        Assert.Equal(2, await db.Feedback.CountAsync());
        Assert.Equal(2, await db.OutboxEvents.CountAsync(x => x.EventType == "NlpFeedbackRecorded.v1"));
        await Assert.ThrowsAsync<Olga.Nlp.Domain.DomainConflictException>(() => repository.SaveAsync(matchResultId, "A", "USEFUL", null, null, null, default));
    }

    [Fact]
    public async Task Evaluation_uses_only_approved_test_samples_and_returns_aggregate_metrics()
    {
        var options = new DbContextOptionsBuilder<NlpDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new NlpDbContext(options);
        db.RankingConfigs.Add(new NlpRankingConfigRow { RankingVersion = "ranking", SemanticWeight = 1, Threshold = .3, ActiveFrom = DateTimeOffset.UtcNow.AddDays(-1) });
        db.EvaluationDatasets.Add(new NlpEvaluationDatasetRow { DatasetId = "approved", Name = "QA", Version = "1", SourcePolicy = "De-identified", Status = "APPROVED", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        db.EvaluationPairs.AddRange(
            new NlpEvaluationPairRow { DatasetId = "approved", RequesterIntentText = "cold-chain storage", CandidateIntentText = "temperature controlled warehouse", GoldLabel = "STRONG", Split = "TEST" },
            new NlpEvaluationPairRow { DatasetId = "approved", RequesterIntentText = "cold-chain storage", CandidateIntentText = "website design", GoldLabel = "NONE", Split = "TEST" },
            new NlpEvaluationPairRow { DatasetId = "approved", RequesterIntentText = "ignored", CandidateIntentText = "ignored", GoldLabel = "STRONG", Split = "TRAIN" });
        await db.SaveChangesAsync();
        var service = new EvaluationService(new EvaluationRepository(db), new FakeEmbeddingProvider(), new TextNormalizer(new PiiChecker()), new ReciprocalScorer(), new RankingConfigRepository(db));

        var response = await service.RunAsync("evaluation-1", new EvaluationRunRequest("approved"), default);

        Assert.Equal("PASSED", response.Status);
        Assert.Equal(2, response.Metrics!["sample_count"]);
        Assert.Contains(await db.OutboxEvents.ToListAsync(), x => x.EventType == "NlpEvaluationRunCompleted.v1" && !x.PayloadJson.Contains("cold-chain", StringComparison.OrdinalIgnoreCase));
    }

    private static Task<IntentResponse> Save(IIntentService service, string member, string id, string type, string text, string category, DateTimeOffset expiry) => service.SaveAsync(member, new IntentUpsertRequest(id, "event", type, text, expiry, category, "pharma", "Selangor"), null, default);
    private sealed class ThrowingEmbeddingProvider : IEmbeddingProvider { public string ModelVersion => "must-not-be-used"; public Task<float[]> EmbedAsync(string text, CancellationToken ct) => throw new InvalidOperationException("Search must reuse stored embeddings."); }
}
