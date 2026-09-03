using Olga.Nlp.Contracts;
using Olga.Nlp.Domain;

namespace Olga.Nlp.Application;

public sealed class MatchingService(ICandidateRepository candidates, IMatchResultRepository results, ITextNormalizer normalizer, IEmbeddingProvider embeddings, IReciprocalScorer scorer, IMatchRanker ranker, RankingConfig config) : IMatchingService
{
    public async Task<MatchSearchResponse> SearchAsync(MatchSearchRequest request, CancellationToken ct)
    {
        var requester = await candidates.GetRequesterIntentAsync(request.MemberId, request.IntentId, request.ContextId, ct) ?? throw new InvalidOperationException("REQUESTER_INTENT_NOT_FOUND");
        var pool = await candidates.GetEligibleCandidatesAsync(request.MemberId, request.ContextId, Math.Clamp(request.Limit * 20, 50, 200), ct);
        var requesterOffer = await embeddings.EmbedAsync(normalizer.Normalize(requester.OriginalText).Value, ct);
        var requesterNeed = await embeddings.EmbedAsync(normalizer.Normalize(requester.OriginalText).Value, ct);
        var scored = new List<(Candidate Candidate, PairScore Score)>();
        foreach (var candidate in pool)
        {
            var offer = await embeddings.EmbedAsync(candidate.Offer.NormalizedText, ct);
            var need = await embeddings.EmbedAsync(candidate.Need.NormalizedText, ct);
            scored.Add((candidate, scorer.Score(requesterNeed, offer, need, requesterOffer)));
        }
        var matches = ranker.Rank(requester, scored, config with { Threshold = request.Options?.Threshold ?? config.Threshold }, Math.Clamp(request.Limit, 3, 7));
        await results.SaveAsync(request.RequestId, request.MemberId, matches, embeddings.ModelVersion, "normalization-v1", config.Version, ct);
        return new(request.RequestId, matches.Select(x => new MatchResponse(x.MemberId, x.Score, x.Label, x.ReasonCodes, x.ReasonText)).ToArray(), embeddings.ModelVersion, "normalization-v1", config.Version);
    }

    public async Task<PairScore> ScorePairAsync(ScorePairRequest request, CancellationToken ct)
    {
        var a = await embeddings.EmbedAsync(normalizer.Normalize(request.RequesterNeed).Value, ct); var b = await embeddings.EmbedAsync(normalizer.Normalize(request.CandidateOffer).Value, ct);
        var c = await embeddings.EmbedAsync(normalizer.Normalize(request.CandidateNeed).Value, ct); var d = await embeddings.EmbedAsync(normalizer.Normalize(request.RequesterOffer).Value, ct);
        return scorer.Score(a, b, c, d);
    }
}
