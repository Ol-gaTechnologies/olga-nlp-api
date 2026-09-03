using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Olga.Nlp.Application;
using Olga.Nlp.Domain;

namespace Olga.Nlp.Infrastructure;

public sealed class PiiChecker : IPiiChecker
{
    private static readonly Regex Pii = new(@"(?:[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|\+?\d[\d\s().-]{7,}\d)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public bool ContainsPii(string text) => Pii.IsMatch(text);
    public string Mask(string text) => Pii.Replace(text, "[REDACTED]");
}
public sealed class TextNormalizer(IPiiChecker pii) : ITextNormalizer
{
    public NormalizedText Normalize(string text, string? requestedLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) throw new ArgumentException("TEXT_INVALID");
        var value = pii.Mask(text.Normalize(NormalizationForm.FormKC)).Trim();
        value = Regex.Replace(value, @"\s+", " ", RegexOptions.CultureInvariant);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        return new(text, value, requestedLanguage ?? "en", hash, pii.ContainsPii(text));
    }
}
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    public string ModelVersion => "fake-embedding-v1";
    public Task<float[]> EmbedAsync(string text, CancellationToken ct) { var v = new float[32]; foreach (var word in Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+")) v[Math.Abs(word.Value.GetHashCode()) % v.Length] += 1; var norm = MathF.Sqrt(v.Sum(x => x * x)); if (norm > 0) for (var i = 0; i < v.Length; i++) v[i] /= norm; return Task.FromResult(v); }
}
public sealed class LocalEmbeddingProvider : IEmbeddingProvider { private readonly FakeEmbeddingProvider inner = new(); public string ModelVersion => "local-placeholder-v1"; public Task<float[]> EmbedAsync(string text, CancellationToken ct) => inner.EmbedAsync(text, ct); }
public sealed class AzureEmbeddingProvider : IEmbeddingProvider { public string ModelVersion => "azure-configured-v1"; public Task<float[]> EmbedAsync(string text, CancellationToken ct) => throw new NotSupportedException("Configure the Azure provider adapter before enabling it."); }
public sealed class ReciprocalScorer : IReciprocalScorer
{
    public PairScore Score(float[] requesterNeed, float[] candidateOffer, float[] candidateNeed, float[] requesterOffer) { var f = Cos(requesterNeed, candidateOffer); var r = Cos(candidateNeed, requesterOffer); var reciprocal = f + r == 0 ? 0 : 2 * f * r / (f + r); return new(f, r, reciprocal, 0.5, 1); }
    private static double Cos(float[] a, float[] b) => Math.Clamp(a.Zip(b).Sum(x => x.First * x.Second), 0, 1);
}
public sealed class ExplanationGenerator : IExplanationGenerator
{
    public (IReadOnlyList<string> Codes, string Text) Explain(Intent requester, Candidate candidate, PairScore score) => score.Reciprocal < .35 ? (Array.Empty<string>(), "") : (new[] { "SEMANTIC_RECIPROCAL" }, $"This member's offer is relevant to the requesting intent, with reciprocal relevance of {score.Reciprocal:0.00}.");
}
public sealed class MatchRanker(IExplanationGenerator explanations) : IMatchRanker
{
    public IReadOnlyList<RankedMatch> Rank(Intent requester, IReadOnlyList<(Candidate Candidate, PairScore Score)> candidates, RankingConfig config, int limit) => candidates.Select(x => { var s = Math.Clamp(config.SemanticWeight * x.Score.Reciprocal + config.CategoryWeight * x.Score.Structured + config.FreshnessWeight * x.Score.Freshness, 0, 1); var e = explanations.Explain(requester, x.Candidate, x.Score); return new RankedMatch(x.Candidate.MemberId, s, s >= .75 ? "strong_match" : "potential_match", e.Codes, e.Text); }).Where(x => x.Score >= config.Threshold && x.ReasonCodes.Count > 0).OrderByDescending(x => x.Score).Take(limit).ToArray();
}
