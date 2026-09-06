using System.Text.Json;
using Olga.Nlp.Contracts;
using Olga.Nlp.Domain;

namespace Olga.Nlp.Application;

public sealed class EvaluationService(
    IEvaluationRepository evaluations,
    IEmbeddingProvider embeddings,
    ITextNormalizer normalizer,
    IReciprocalScorer scorer,
    IRankingConfigRepository rankingConfigs) : IEvaluationService
{
    public async Task<EvaluationRunResponse> RunAsync(string evaluationRunId, EvaluationRunRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(evaluationRunId)) throw new ArgumentException("IDEMPOTENCY_KEY_REQUIRED");
        if (string.IsNullOrWhiteSpace(request.DatasetId)) throw new ArgumentException("EVALUATION_DATASET_REQUIRED");

        var ranking = await rankingConfigs.GetActiveAsync(ct);
        var execution = await evaluations.TryStartAsync(evaluationRunId, request.DatasetId, embeddings.ModelVersion, ranking.Version, ct);
        if (execution.Status != "RUNNING") return ToResponse(execution);

        try
        {
            var samples = await evaluations.GetApprovedSamplesAsync(request.DatasetId, ct);
            if (samples.Count == 0) throw new DomainNotFoundException("EVALUATION_DATASET_EMPTY");

            var scores = new List<(bool Positive, bool Predicted, double Score)>(samples.Count);
            foreach (var sample in samples)
            {
                var requester = await embeddings.EmbedAsync(normalizer.Normalize(sample.RequesterIntentText).Value, ct);
                var candidate = await embeddings.EmbedAsync(normalizer.Normalize(sample.CandidateIntentText).Value, ct);
                var score = scorer.Score(requester, candidate, null, null).Forward;
                var positive = sample.GoldLabel is "STRONG" or "PLAUSIBLE";
                scores.Add((positive, score >= ranking.Threshold, score));
            }

            var truePositive = scores.Count(x => x.Positive && x.Predicted);
            var falsePositive = scores.Count(x => !x.Positive && x.Predicted);
            var falseNegative = scores.Count(x => x.Positive && !x.Predicted);
            var predicted = truePositive + falsePositive;
            var positives = truePositive + falseNegative;
            var metrics = new Dictionary<string, double>
            {
                ["sample_count"] = scores.Count,
                ["threshold"] = ranking.Threshold,
                ["precision_at_threshold"] = predicted == 0 ? 0 : (double)truePositive / predicted,
                ["recall_at_threshold"] = positives == 0 ? 0 : (double)truePositive / positives,
                ["coverage"] = (double)predicted / scores.Count,
                ["mean_similarity"] = scores.Average(x => x.Score)
            };
            var metricsJson = JsonSerializer.Serialize(metrics);
            await evaluations.CompleteAsync(evaluationRunId, metricsJson, ct);
            return ToResponse((await evaluations.GetAsync(evaluationRunId, ct))!);
        }
        catch
        {
            await evaluations.FailAsync(evaluationRunId, ct);
            throw;
        }
    }

    public async Task<EvaluationRunResponse?> GetAsync(string evaluationRunId, CancellationToken ct)
    {
        var execution = await evaluations.GetAsync(evaluationRunId, ct);
        return execution is null ? null : ToResponse(execution);
    }

    private static EvaluationRunResponse ToResponse(EvaluationExecution execution) => new(
        execution.EvaluationRunId, execution.DatasetId, execution.ModelVersion, execution.RankingVersion,
        execution.Status, DeserializeMetrics(execution.MetricsJson), execution.StartedAt, execution.CompletedAt);

    private static IReadOnlyDictionary<string, double>? DeserializeMetrics(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Dictionary<string, double>>(json);
}
