using Microsoft.EntityFrameworkCore;
using Olga.Nlp.Application;
using Olga.Nlp.Domain;

namespace Olga.Nlp.Infrastructure;

public sealed class CandidateRepository(NlpDbContext db) : ICandidateRepository
{
    public async Task<Intent?> GetRequesterIntentAsync(string memberId, string intentId, string contextId, CancellationToken ct) { var x = await db.Intents.AsNoTracking().SingleOrDefaultAsync(x => x.IntentId == intentId && x.MemberId == memberId && x.ContextId == contextId && x.Status == "ACTIVE" && x.ExpiresAt > DateTimeOffset.UtcNow, ct); return x is null ? null : Map(x); }
    public async Task<IReadOnlyList<Candidate>> GetEligibleCandidatesAsync(string requesterId, string contextId, int maxRows, CancellationToken ct) { var rows = await db.Intents.AsNoTracking().Where(x => x.MemberId != requesterId && x.ContextId == contextId && x.Status == "ACTIVE" && x.ExpiresAt > DateTimeOffset.UtcNow).OrderByDescending(x => x.ExpiresAt).Take(maxRows).ToListAsync(ct); return rows.GroupBy(x => x.MemberId).Select(g => { var a = g.ToArray(); return new Candidate(g.Key, Map(a.FirstOrDefault(x => x.IntentType == IntentType.Offer) ?? a[0]), Map(a.FirstOrDefault(x => x.IntentType == IntentType.Need) ?? a[0])); }).ToArray(); }
    private static Intent Map(NlpIntentRow x) => new(x.IntentId, x.MemberId, x.ContextId, x.IntentType, x.OriginalText, x.NormalizedText, x.ExpiresAt, x.Status);
}
public sealed class MatchResultRepository(NlpDbContext db) : IMatchResultRepository
{
    public async Task SaveAsync(string requestId, string requesterId, IReadOnlyList<RankedMatch> matches, string modelVersion, string preprocessingVersion, string rankingVersion, CancellationToken ct) { if (await db.MatchResults.AnyAsync(x => x.RequestId == requestId, ct)) return; await using var tx = await db.Database.BeginTransactionAsync(ct); foreach (var m in matches) db.MatchResults.Add(new() { RequestId = requestId, RequesterId = requesterId, CandidateId = m.MemberId, Score = m.Score, ReasonCodes = string.Join(',', m.ReasonCodes), ReasonText = m.ReasonText, ModelVersion = modelVersion, PreprocessingVersion = preprocessingVersion, RankingVersion = rankingVersion, CreatedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
    public async Task SaveFeedbackAsync(string requestId, string requesterId, string candidateId, string label, string? reason, CancellationToken ct) => await Task.CompletedTask;
}
