using Olga.Nlp.Domain;

namespace Olga.Nlp.Application;

public interface ITextNormalizer { NormalizedText Normalize(string text, string? requestedLanguage = null); }
public interface IPiiChecker { bool ContainsPii(string text); string Mask(string text); }
public interface IEmbeddingProvider { string ModelVersion { get; } Task<float[]> EmbedAsync(string text, CancellationToken ct); }
public interface IReciprocalScorer { PairScore Score(float[] requesterNeed, float[] candidateOffer, float[] candidateNeed, float[] requesterOffer); }
public interface IMatchRanker { IReadOnlyList<RankedMatch> Rank(Intent requester, IReadOnlyList<(Candidate Candidate, PairScore Score)> candidates, RankingConfig config, int limit); }
public interface IExplanationGenerator { (IReadOnlyList<string> Codes, string Text) Explain(Intent requester, Candidate candidate, PairScore score); }
public interface ICandidateRepository { Task<Intent?> GetRequesterIntentAsync(string memberId, string intentId, string contextId, CancellationToken ct); Task<IReadOnlyList<Candidate>> GetEligibleCandidatesAsync(string requesterId, string contextId, int maxRows, CancellationToken ct); }
public interface IMatchResultRepository { Task SaveAsync(string requestId, string requesterId, IReadOnlyList<RankedMatch> matches, string modelVersion, string preprocessingVersion, string rankingVersion, CancellationToken ct); Task SaveFeedbackAsync(string requestId, string requesterId, string candidateId, string label, string? reason, CancellationToken ct); }
public interface IMatchingService { Task<Olga.Nlp.Contracts.MatchSearchResponse> SearchAsync(Olga.Nlp.Contracts.MatchSearchRequest request, CancellationToken ct); Task<Olga.Nlp.Domain.PairScore> ScorePairAsync(Olga.Nlp.Contracts.ScorePairRequest request, CancellationToken ct); }
