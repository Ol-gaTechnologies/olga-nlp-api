namespace Olga.Nlp.Domain;

public enum IntentType { Offer, Want }
public enum IntentStatus { Processing, MatchReady, Failed, Inactive }
public enum MatchExecutionStatus { Processing, Completed, Failed }
public enum MatchStartDisposition { Started, Replay, InProgress, Conflict }

public sealed record Intent(
    string IntentId,
    string MemberId,
    string ContextId,
    IntentType Type,
    string OriginalText,
    string NormalizedText,
    DateTimeOffset ExpiresAt,
    IntentStatus Status,
    float[]? Embedding,
    string? ModelVersion,
    string PreprocessingVersion,
    string? Category = null,
    string? Industry = null,
    string? Geography = null,
    string NormalizedHash = "",
    string LanguageCode = "en",
    bool ContainsPii = false,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset UpdatedAt = default,
    string ETag = "");

public sealed record MemberIntents(string MemberId, Intent? Offer, Intent? Want);
public sealed record Candidate(string MemberId, Intent Offer, Intent? Want, double CategoryCompatibility, double IndustryCompatibility, double GeographyCompatibility, double Freshness);
public sealed record NormalizedText(string Original, string Value, string Language, string Hash, bool ContainsPii);
public sealed record PairScore(double Forward, double? Reverse, double Reciprocal, double Semantic);

public sealed record RankedMatch(
    string MemberId,
    double Score,
    string Label,
    IReadOnlyList<string> ReasonCodes,
    string ReasonText,
    double SemanticScore = 0,
    double? ReciprocalScore = null,
    int Rank = 0,
    long? MatchResultId = null);

public sealed record RankingConfig(
    string Version,
    double SemanticWeight = .40,
    double CategoryWeight = .25,
    double IndustryWeight = .15,
    double GeographyWeight = .10,
    double FreshnessWeight = .10,
    double Threshold = .35,
    double EventWeight = 0,
    string? ConfigJson = null);

public sealed record MatchExecution(
    string RequestId,
    string RequestHash,
    string RequesterId,
    string IntentId,
    string ContextId,
    string LanguageCode,
    int RequestedLimit,
    string? RequestOptionsJson,
    MatchExecutionStatus Status,
    string? PreprocessingVersion = null,
    string? ModelVersion = null,
    string? RankingVersion = null,
    double? RankingThreshold = null,
    int? CandidateCount = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? CompletedAt = null,
    string? ErrorCode = null);

public sealed record MatchStartResult(MatchStartDisposition Disposition, MatchExecution Execution);
public sealed record FeedbackRecord(long FeedbackId, long MatchResultId, string RequesterId, string Label, DateTimeOffset CreatedAt);
public sealed record EvaluationSample(long EvaluationPairId, string RequesterIntentText, string CandidateIntentText, string GoldLabel);
public sealed record EvaluationExecution(string EvaluationRunId, string DatasetId, string ModelVersion, string RankingVersion, string Status, string? MetricsJson, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

public sealed class DomainConflictException(string code) : Exception(code) { public string Code { get; } = code; }
public sealed class DomainNotFoundException(string code) : Exception(code) { public string Code { get; } = code; }
