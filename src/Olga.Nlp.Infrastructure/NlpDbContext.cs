using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Olga.Nlp.Infrastructure;

public sealed class NlpDbContext(DbContextOptions<NlpDbContext> options) : DbContext(options)
{
    public DbSet<NlpIntentRow> Intents => Set<NlpIntentRow>();
    public DbSet<NlpEmbeddingRow> Embeddings => Set<NlpEmbeddingRow>();
    public DbSet<NlpModelVersionRow> ModelVersions => Set<NlpModelVersionRow>();
    public DbSet<NlpRankingConfigRow> RankingConfigs => Set<NlpRankingConfigRow>();
    public DbSet<NlpProcessingJobRow> ProcessingJobs => Set<NlpProcessingJobRow>();
    public DbSet<NlpMatchRequestRow> MatchRequests => Set<NlpMatchRequestRow>();
    public DbSet<NlpMatchResultRow> MatchResults => Set<NlpMatchResultRow>();
    public DbSet<NlpFeedbackRow> Feedback => Set<NlpFeedbackRow>();
    public DbSet<NlpMatchSuppressionRow> MatchSuppressions => Set<NlpMatchSuppressionRow>();
    public DbSet<NlpEvaluationDatasetRow> EvaluationDatasets => Set<NlpEvaluationDatasetRow>();
    public DbSet<NlpEvaluationPairRow> EvaluationPairs => Set<NlpEvaluationPairRow>();
    public DbSet<NlpEvaluationRunRow> EvaluationRuns => Set<NlpEvaluationRunRow>();
    public DbSet<OutboxEventRow> OutboxEvents => Set<OutboxEventRow>();

    // Production reads these projections. Their source tables remain owned by Core API domains.
    public DbSet<MemberContextEligibilityProjectionRow> EligibilityProjection => Set<MemberContextEligibilityProjectionRow>();
    public DbSet<MemberRelationshipProjectionRow> RelationshipProjection => Set<MemberRelationshipProjectionRow>();

    // Stand-alone writable fixtures are used only by the local InMemory test profile.
    public DbSet<MemberContextEligibilityRow> MemberEligibility => Set<MemberContextEligibilityRow>();
    public DbSet<MemberRelationshipRow> MemberRelationships => Set<MemberRelationshipRow>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("nlp");
        b.Entity<NlpIntentRow>(e =>
        {
            e.ToTable("NlpIntent");
            e.HasKey(x => x.IntentId);
            e.HasIndex(x => new { x.ContextId, x.IntentType, x.Status, x.ExpiresAt });
            e.HasIndex(x => new { x.MemberId, x.Status });
            e.HasIndex(x => x.NormalizedHash);
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        b.Entity<NlpEmbeddingRow>(e =>
        {
            e.ToTable("NlpEmbedding");
            e.HasKey(x => new { x.IntentId, x.ModelVersion });
            e.HasIndex(x => new { x.IntentId, x.NormalizedHash, x.ModelVersion }).IsUnique();
        });
        b.Entity<NlpModelVersionRow>(e => { e.ToTable("NlpModelVersion"); e.HasKey(x => x.ModelVersion); });
        b.Entity<NlpRankingConfigRow>(e => { e.ToTable("NlpRankingConfig"); e.HasKey(x => x.RankingVersion); });
        b.Entity<NlpProcessingJobRow>(e =>
        {
            e.ToTable("NlpProcessingJob");
            e.HasKey(x => x.JobId);
            e.HasIndex(x => new { x.Status, x.AvailableAt });
            e.HasIndex(x => new { x.IntentId, x.JobType }).IsUnique().HasFilter("[status] IN ('PENDING','RUNNING','FAILED')");
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        b.Entity<NlpMatchRequestRow>(e =>
        {
            e.ToTable("MatchRequest");
            e.HasKey(x => x.RequestId);
            e.HasIndex(x => new { x.RequesterId, x.CreatedAt });
            e.Property(x => x.RowVersion).IsRowVersion();
        });
        b.Entity<NlpMatchResultRow>(e =>
        {
            e.ToTable("NlpMatchResult");
            e.HasKey(x => x.MatchResultId);
            e.HasIndex(x => new { x.RequestId, x.CandidateId }).IsUnique();
            e.HasIndex(x => new { x.RequestId, x.Rank }).IsUnique();
        });
        b.Entity<NlpFeedbackRow>(e =>
        {
            e.ToTable("NlpFeedback");
            e.HasKey(x => x.FeedbackId);
            e.HasIndex(x => new { x.MatchResultId, x.RequesterId }).IsUnique().HasFilter("[supersedes_feedback_id] IS NULL");
            e.HasIndex(x => x.SupersedesFeedbackId).IsUnique().HasFilter("[supersedes_feedback_id] IS NOT NULL");
        });
        b.Entity<NlpMatchSuppressionRow>(e =>
        {
            e.ToTable("MatchSuppression");
            e.HasKey(x => x.SuppressionId);
            e.HasIndex(x => new { x.MemberId, x.ContextId, x.EndsAt });
            e.HasIndex(x => new { x.IntentId, x.EndsAt });
        });
        b.Entity<NlpEvaluationDatasetRow>(e => { e.ToTable("EvaluationDataset"); e.HasKey(x => x.DatasetId); e.HasIndex(x => new { x.Name, x.Version }).IsUnique(); e.Property(x => x.RowVersion).IsRowVersion(); });
        b.Entity<NlpEvaluationPairRow>(e => { e.ToTable("EvaluationPair"); e.HasKey(x => x.EvaluationPairId); e.HasIndex(x => new { x.DatasetId, x.Split }); });
        b.Entity<NlpEvaluationRunRow>(e => { e.ToTable("EvaluationRun"); e.HasKey(x => x.EvaluationRunId); e.HasIndex(x => new { x.DatasetId, x.StartedAt }); e.HasIndex(x => new { x.ModelVersion, x.RankingVersion }); });
        b.Entity<OutboxEventRow>(e => { e.ToTable("OutboxEvent", "ops"); e.HasKey(x => x.OutboxEventId); e.HasIndex(x => new { x.PublishedAt, x.NextAttemptAt }); });

        b.Entity<MemberContextEligibilityProjectionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_MemberContextEligibility", "nlp");
        });
        b.Entity<MemberRelationshipProjectionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_MemberRelationship", "nlp");
        });

        b.Entity<MemberContextEligibilityRow>(e => { e.ToTable("MemberContextEligibility_Local"); e.HasKey(x => new { x.MemberId, x.ContextId }); });
        b.Entity<MemberRelationshipRow>(e => { e.ToTable("MemberRelationship_Local"); e.HasKey(x => new { x.MemberId, x.OtherMemberId, x.ContextId }); });

        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
    }

    private static string ToSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i])) result.Append('_');
            result.Append(char.ToLowerInvariant(value[i]));
        }
        return result.ToString();
    }
}

public sealed class NlpIntentRow
{
    public string IntentId { get; set; } = "";
    public string MemberId { get; set; } = "";
    public string ContextId { get; set; } = "";
    public string IntentType { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public string NormalizedText { get; set; } = "";
    public string NormalizedHash { get; set; } = "";
    public string LanguageCode { get; set; } = "en";
    public bool ContainsPii { get; set; }
    public string Status { get; set; } = "PROCESSING";
    public string? Category { get; set; }
    public string? Industry { get; set; }
    public string? Geography { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string PreprocessingVersion { get; set; } = "normalizer-v1";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NlpEmbeddingRow
{
    public string IntentId { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public int Dimensions { get; set; }
    public string NormalizedHash { get; set; } = "";
    public byte[] Embedding { get; set; } = [];
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NlpModelVersionRow
{
    public string ModelVersion { get; set; } = "";
    public string Provider { get; set; } = "";
    public string DeploymentName { get; set; } = "";
    public int Dimensions { get; set; }
    public string PreprocessingVersion { get; set; } = "";
    public string Status { get; set; } = "CANDIDATE";
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NlpRankingConfigRow
{
    public string RankingVersion { get; set; } = "";
    public double SemanticWeight { get; set; }
    public double CategoryWeight { get; set; }
    public double IndustryWeight { get; set; }
    public double GeographyWeight { get; set; }
    public double FreshnessWeight { get; set; }
    public double EventWeight { get; set; }
    public double Threshold { get; set; }
    public string? ConfigJson { get; set; }
    public DateTimeOffset ActiveFrom { get; set; }
    public DateTimeOffset? ActiveTo { get; set; }
}

public sealed class NlpProcessingJobRow
{
    public string JobId { get; set; } = "";
    public string IntentId { get; set; } = "";
    public string JobType { get; set; } = "EMBED";
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NlpMatchRequestRow
{
    public string RequestId { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string IntentId { get; set; } = "";
    public string ContextId { get; set; } = "";
    public string LanguageCode { get; set; } = "en";
    public short RequestedLimit { get; set; }
    public string? RequestOptionsJson { get; set; }
    public string Status { get; set; } = "PROCESSING";
    public string? PreprocessingVersion { get; set; }
    public string? ModelVersion { get; set; }
    public string? RankingVersion { get; set; }
    public double? RankingThreshold { get; set; }
    public int? CandidateCount { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NlpMatchResultRow
{
    public long MatchResultId { get; set; }
    public string RequestId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string CandidateId { get; set; } = "";
    public short Rank { get; set; }
    public double SemanticScore { get; set; }
    public double? ReciprocalScore { get; set; }
    public double FinalScore { get; set; }
    public string Label { get; set; } = "";
    public string ReasonCodes { get; set; } = "[]";
    public string ReasonText { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string PreprocessingVersion { get; set; } = "";
    public string RankingVersion { get; set; } = "";
    public string PolicyStatus { get; set; } = "ELIGIBLE";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NlpFeedbackRow
{
    public long FeedbackId { get; set; }
    public long? SupersedesFeedbackId { get; set; }
    public long MatchResultId { get; set; }
    public string RequestId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string CandidateId { get; set; } = "";
    public string Label { get; set; } = "";
    public string? ReasonCode { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NlpMatchSuppressionRow
{
    public long SuppressionId { get; set; }
    public string? MemberId { get; set; }
    public string? IntentId { get; set; }
    public string? ContextId { get; set; }
    public string ReasonCode { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class NlpEvaluationDatasetRow
{
    public string DatasetId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Description { get; set; }
    public string SourcePolicy { get; set; } = "";
    public string Status { get; set; } = "DRAFT";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class NlpEvaluationPairRow
{
    public long EvaluationPairId { get; set; }
    public string DatasetId { get; set; } = "";
    public string RequesterIntentText { get; set; } = "";
    public string CandidateIntentText { get; set; } = "";
    public string? StructuredFeaturesJson { get; set; }
    public string GoldLabel { get; set; } = "";
    public string Split { get; set; } = "TEST";
    public string? OrganizationGroup { get; set; }
    public string? LabelReason { get; set; }
}

public sealed class NlpEvaluationRunRow
{
    public string EvaluationRunId { get; set; } = "";
    public string DatasetId { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string RankingVersion { get; set; } = "";
    public string Status { get; set; } = "RUNNING";
    public string? MetricsJson { get; set; }
    public string? ErrorReportBlobPath { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class OutboxEventRow
{
    public string OutboxEventId { get; set; } = "";
    public string AggregateType { get; set; } = "";
    public string AggregateId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public class MemberContextEligibilityProjectionRow
{
    public string MemberId { get; set; } = "";
    public string ContextId { get; set; } = "";
    public bool IsLive { get; set; }
    public bool IsVisible { get; set; }
    public bool HasConsent { get; set; }
    public bool IsSuspended { get; set; }
    public bool IsDeleted { get; set; }
}

public sealed class MemberRelationshipProjectionRow
{
    public string MemberId { get; set; } = "";
    public string OtherMemberId { get; set; } = "";
    public string ContextId { get; set; } = "";
    public bool IsBlocked { get; set; }
    public bool IsConnected { get; set; }
}

public sealed class MemberContextEligibilityRow
{
    public string MemberId { get; set; } = "";
    public string ContextId { get; set; } = "";
    public bool IsLive { get; set; }
    public bool IsVisible { get; set; }
    public bool HasConsent { get; set; }
    public bool IsSuspended { get; set; }
    public bool IsDeleted { get; set; }
}
public sealed class MemberRelationshipRow
{
    public string MemberId { get; set; } = "";
    public string OtherMemberId { get; set; } = "";
    public string ContextId { get; set; } = "";
    public bool IsBlocked { get; set; }
    public bool IsConnected { get; set; }
}
