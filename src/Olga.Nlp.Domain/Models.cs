namespace Olga.Nlp.Domain;

public enum IntentType { Offer, Need }
public sealed record Intent(string IntentId, string MemberId, string ContextId, IntentType Type, string OriginalText, string NormalizedText, DateTimeOffset ExpiresAt, string Status = "ACTIVE");
public sealed record Candidate(string MemberId, Intent Offer, Intent Need, double Freshness = 1.0, string? Industry = null, string? Category = null);
public sealed record NormalizedText(string Original, string Value, string Language, string Hash, bool ContainsPii);
public sealed record PairScore(double Forward, double Reverse, double Reciprocal, double Structured, double Freshness);
public sealed record RankedMatch(string MemberId, double Score, string Label, IReadOnlyList<string> ReasonCodes, string ReasonText);
public sealed record RankingConfig(string Version, double CategoryWeight = .40, double SemanticWeight = .25, double IndustryWeight = .15, double SeniorityWeight = .10, double FreshnessWeight = .10, double Threshold = .35);
