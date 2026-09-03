namespace Olga.Nlp.Contracts;

public sealed record MatchSearchRequest(string RequestId, string MemberId, string IntentId, string ContextId, int Limit = 7, MatchOptions? Options = null);
public sealed record MatchOptions(double? Threshold = null, string? Language = null);
public sealed record MatchSearchResponse(string RequestId, IReadOnlyList<MatchResponse> Matches, string ModelVersion, string PreprocessingVersion, string RankingVersion);
public sealed record MatchResponse(string MemberId, double Score, string Label, IReadOnlyList<string> ReasonCodes, string ReasonText);
public sealed record ScorePairRequest(string RequesterOffer, string RequesterNeed, string CandidateOffer, string CandidateNeed);
public sealed record FeedbackRequest(string RequestId, string RequesterId, string CandidateId, string Label, string? Reason);
public sealed record NormalizeRequest(string Text, string? Language);
public sealed record NormalizeResponse(string NormalizedText, string Language, string Hash, bool ContainsPii);
