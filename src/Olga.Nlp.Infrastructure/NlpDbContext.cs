using System.Text;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pgvector;

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
        b.HasPostgresExtension("vector");
        b.Entity<NlpIntentRow>(e =>
        {
            e.ToTable("nlp_intent");
            e.HasKey(x => x.IntentId);
            e.HasIndex(x => new { x.ContextId, x.IntentType, x.Status, x.ExpiresAt }).HasDatabaseName("ix_nlp_intent_context_type_status_expiry");
            e.HasIndex(x => new { x.MemberId, x.Status }).HasDatabaseName("ix_nlp_intent_member_status");
            e.HasIndex(x => x.NormalizedHash).HasDatabaseName("ix_nlp_intent_normalized_hash");
            e.Property(x => x.IntentId).HasMaxLength(64);
            e.Property(x => x.MemberId).HasMaxLength(64);
            e.Property(x => x.ContextId).HasMaxLength(64);
            e.Property(x => x.IntentType).HasMaxLength(16);
            e.Property(x => x.OriginalText).HasMaxLength(4000);
            e.Property(x => x.NormalizedText).HasMaxLength(4000);
            e.Property(x => x.NormalizedHash).HasColumnType("character(64)").IsFixedLength();
            e.Property(x => x.LanguageCode).HasMaxLength(16).HasDefaultValue("en");
            e.Property(x => x.ContainsPii).HasDefaultValue(false);
            e.Property(x => x.Status).HasMaxLength(32).HasDefaultValue("PROCESSING");
            e.Property(x => x.Category).HasMaxLength(128);
            e.Property(x => x.Industry).HasMaxLength(128);
            e.Property(x => x.Geography).HasMaxLength(128);
            e.Property(x => x.PreprocessingVersion).HasMaxLength(128);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.RowVersion).HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        });
        b.Entity<NlpEmbeddingRow>(e =>
        {
            e.ToTable("nlp_embedding");
            e.HasKey(x => new { x.IntentId, x.ModelVersion });
            e.HasIndex(x => new { x.IntentId, x.NormalizedHash, x.ModelVersion }).IsUnique().HasDatabaseName("uq_nlp_embedding_intent_hash_model");
            e.Property(x => x.IntentId).HasMaxLength(64);
            e.Property(x => x.ModelVersion).HasMaxLength(128);
            e.Property(x => x.NormalizedHash).HasColumnType("character(64)").IsFixedLength();
            var embedding = e.Property(x => x.Embedding);
            if (Database.IsNpgsql()) embedding.HasColumnType("vector(1536)");
            else embedding.HasConversion(value => SerializeVector(value), value => DeserializeVector(value));
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("ACTIVE");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
        b.Entity<NlpModelVersionRow>(e =>
        {
            e.ToTable("nlp_model_version");
            e.HasKey(x => x.ModelVersion);
            e.Property(x => x.ModelVersion).HasMaxLength(128);
            e.Property(x => x.Provider).HasMaxLength(64);
            e.Property(x => x.DeploymentName).HasMaxLength(128);
            e.Property(x => x.PreprocessingVersion).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("CANDIDATE");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.RowVersion).HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        });
        b.Entity<NlpRankingConfigRow>(e =>
        {
            e.ToTable("nlp_ranking_config");
            e.HasKey(x => x.RankingVersion);
            e.Property(x => x.RankingVersion).HasMaxLength(128);
            e.Property(x => x.SemanticWeight).HasPrecision(6, 5).HasDefaultValue(0.40m);
            e.Property(x => x.CategoryWeight).HasPrecision(6, 5).HasDefaultValue(0.25m);
            e.Property(x => x.IndustryWeight).HasPrecision(6, 5).HasDefaultValue(0.15m);
            e.Property(x => x.GeographyWeight).HasPrecision(6, 5).HasDefaultValue(0.10m);
            e.Property(x => x.FreshnessWeight).HasPrecision(6, 5).HasDefaultValue(0.10m);
            e.Property(x => x.EventWeight).HasPrecision(6, 5).HasDefaultValue(0m);
            e.Property(x => x.Threshold).HasPrecision(6, 5).HasDefaultValue(0.35m);
            e.Property(x => x.ConfigJson).HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.RowVersion).HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        });
        b.Entity<NlpProcessingJobRow>(e =>
        {
            e.ToTable("nlp_processing_job");
            e.HasKey(x => x.JobId);
            e.HasIndex(x => new { x.Status, x.AvailableAt }).HasDatabaseName("ix_nlp_processing_job_status_available");
            e.HasIndex(x => new { x.IntentId, x.JobType }).IsUnique().HasFilter("status IN ('PENDING','RUNNING','FAILED')").HasDatabaseName("uq_nlp_processing_job_active_intent_type");
            e.Property(x => x.JobId).HasMaxLength(64);
            e.Property(x => x.IntentId).HasMaxLength(64);
            e.Property(x => x.JobType).HasMaxLength(24).HasDefaultValue("EMBED");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("PENDING");
            e.Property(x => x.AttemptCount).HasDefaultValue(0);
            e.Property(x => x.AvailableAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.ErrorCode).HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.RowVersion).HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        });
        b.Entity<NlpMatchRequestRow>(e =>
        {
            e.ToTable("match_request");
            e.HasKey(x => x.RequestId);
            e.HasIndex(x => new { x.RequesterId, x.CreatedAt }).HasDatabaseName("ix_match_request_requester_created");
            e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.RequestHash).HasColumnType("character(64)").IsFixedLength();
            e.Property(x => x.RequesterId).HasMaxLength(64);
            e.Property(x => x.IntentId).HasMaxLength(64);
            e.Property(x => x.ContextId).HasMaxLength(64);
            e.Property(x => x.LanguageCode).HasMaxLength(16).HasDefaultValue("en");
            e.Property(x => x.RequestedLimit).HasDefaultValue((short)7);
            e.Property(x => x.RequestOptionsJson).HasColumnType("jsonb");
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("PROCESSING");
            e.Property(x => x.PreprocessingVersion).HasMaxLength(128);
            e.Property(x => x.ModelVersion).HasMaxLength(128);
            e.Property(x => x.RankingVersion).HasMaxLength(128);
            e.Property(x => x.RankingThreshold).HasPrecision(6, 5);
            e.Ignore(x => x.ErrorCode);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.RowVersion).HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        });
        b.Entity<NlpMatchResultRow>(e =>
        {
            e.ToTable("nlp_match_result");
            e.HasKey(x => x.MatchResultId);
            e.HasIndex(x => new { x.RequestId, x.CandidateId }).IsUnique().HasDatabaseName("uq_nlp_match_result_request_candidate");
            e.HasIndex(x => new { x.RequestId, x.Rank }).IsUnique().HasDatabaseName("uq_nlp_match_result_request_rank");
            e.Property(x => x.MatchResultId).UseIdentityByDefaultColumn();
            e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.RequesterId).HasMaxLength(64);
            e.Property(x => x.CandidateId).HasMaxLength(64);
            e.Property(x => x.SemanticScore).HasPrecision(8, 7);
            e.Property(x => x.ReciprocalScore).HasPrecision(8, 7);
            e.Property(x => x.FinalScore).HasPrecision(8, 7);
            e.Property(x => x.Label).HasMaxLength(32);
            e.Property(x => x.ReasonCodes).HasColumnType("jsonb");
            e.Property(x => x.ReasonText).HasMaxLength(2000);
            e.Property(x => x.ModelVersion).HasMaxLength(128);
            e.Property(x => x.PreprocessingVersion).HasMaxLength(128);
            e.Property(x => x.RankingVersion).HasMaxLength(128);
            e.Property(x => x.PolicyStatus).HasMaxLength(24).HasDefaultValue("ELIGIBLE");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
        b.Entity<NlpFeedbackRow>(e =>
        {
            e.ToTable("nlp_feedback");
            e.HasKey(x => x.FeedbackId);
            e.HasIndex(x => new { x.MatchResultId, x.RequesterId }).IsUnique().HasFilter("supersedes_feedback_id IS NULL").HasDatabaseName("uq_nlp_feedback_initial_match_requester");
            e.HasIndex(x => x.SupersedesFeedbackId).IsUnique().HasFilter("supersedes_feedback_id IS NOT NULL").HasDatabaseName("uq_nlp_feedback_supersedes");
            e.Property(x => x.FeedbackId).UseIdentityByDefaultColumn();
            e.Property(x => x.RequestId).HasMaxLength(64);
            e.Property(x => x.RequesterId).HasMaxLength(64);
            e.Property(x => x.CandidateId).HasMaxLength(64);
            e.Property(x => x.Label).HasMaxLength(64);
            e.Property(x => x.ReasonCode).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
        b.Entity<NlpMatchSuppressionRow>(e =>
        {
            e.ToTable("match_suppression");
            e.HasKey(x => x.SuppressionId);
            e.HasIndex(x => new { x.MemberId, x.ContextId, x.EndsAt }).HasDatabaseName("ix_match_suppression_member_context_end");
            e.HasIndex(x => new { x.IntentId, x.EndsAt }).HasDatabaseName("ix_match_suppression_intent_end");
            e.Property(x => x.SuppressionId).UseIdentityByDefaultColumn();
            e.Property(x => x.MemberId).HasMaxLength(64);
            e.Property(x => x.IntentId).HasMaxLength(64);
            e.Property(x => x.ContextId).HasMaxLength(64);
            e.Property(x => x.ReasonCode).HasMaxLength(64);
            e.Property(x => x.StartsAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.CreatedBy).HasMaxLength(64);
        });
        b.Entity<NlpEvaluationDatasetRow>(e =>
        {
            e.ToTable("evaluation_dataset");
            e.HasKey(x => x.DatasetId);
            e.HasIndex(x => new { x.Name, x.Version }).IsUnique().HasDatabaseName("uq_evaluation_dataset_name_version");
            e.Property(x => x.DatasetId).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Version).HasMaxLength(32);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.SourcePolicy).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("DRAFT");
            e.Property(x => x.ApprovedBy).HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.RowVersion).HasDefaultValue(1L).IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
        });
        b.Entity<NlpEvaluationPairRow>(e =>
        {
            e.ToTable("evaluation_pair");
            e.HasKey(x => x.EvaluationPairId);
            e.HasIndex(x => new { x.DatasetId, x.Split }).HasDatabaseName("ix_evaluation_pair_dataset_split");
            e.Property(x => x.EvaluationPairId).UseIdentityByDefaultColumn();
            e.Property(x => x.DatasetId).HasMaxLength(64);
            e.Property(x => x.RequesterIntentText).HasMaxLength(4000);
            e.Property(x => x.CandidateIntentText).HasMaxLength(4000);
            e.Property(x => x.StructuredFeaturesJson).HasColumnType("jsonb");
            e.Property(x => x.GoldLabel).HasMaxLength(32);
            e.Property(x => x.Split).HasMaxLength(16);
            e.Property(x => x.OrganizationGroup).HasMaxLength(64);
            e.Property(x => x.LabelReason).HasMaxLength(1000);
        });
        b.Entity<NlpEvaluationRunRow>(e =>
        {
            e.ToTable("evaluation_run");
            e.HasKey(x => x.EvaluationRunId);
            e.HasIndex(x => new { x.DatasetId, x.StartedAt }).HasDatabaseName("ix_evaluation_run_dataset_started");
            e.HasIndex(x => new { x.ModelVersion, x.RankingVersion }).HasDatabaseName("ix_evaluation_run_model_ranking");
            e.Property(x => x.EvaluationRunId).HasMaxLength(64);
            e.Property(x => x.DatasetId).HasMaxLength(64);
            e.Property(x => x.ModelVersion).HasMaxLength(128);
            e.Property(x => x.RankingVersion).HasMaxLength(128);
            e.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("RUNNING");
            e.Property(x => x.MetricsJson).HasColumnType("jsonb");
            e.Property(x => x.ErrorReportBlobPath).HasMaxLength(1024);
            e.Property(x => x.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });
        b.Entity<OutboxEventRow>(e =>
        {
            e.ToTable("outbox_event", "ops");
            e.HasKey(x => x.OutboxEventId);
            e.HasIndex(x => new { x.PublishedAt, x.NextAttemptAt }).HasDatabaseName("ix_outbox_event_publish_schedule");
            e.Property(x => x.OutboxEventId).HasMaxLength(64);
            e.Property(x => x.AggregateType).HasMaxLength(64);
            e.Property(x => x.AggregateId).HasMaxLength(64);
            e.Property(x => x.EventType).HasMaxLength(128);
            e.Property(x => x.PayloadJson).HasColumnType("jsonb");
            e.Property(x => x.OccurredAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            e.Property(x => x.AttemptCount).HasDefaultValue(0);
        });

        b.Entity<MemberContextEligibilityProjectionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_member_context_eligibility", "nlp");
            e.Property(x => x.MemberId).HasMaxLength(64);
            e.Property(x => x.ContextId).HasMaxLength(64);
        });
        b.Entity<MemberRelationshipProjectionRow>(e =>
        {
            e.HasNoKey();
            e.ToView("vw_member_relationship", "nlp");
            e.Property(x => x.MemberId).HasMaxLength(64);
            e.Property(x => x.OtherMemberId).HasMaxLength(64);
            e.Property(x => x.ContextId).HasMaxLength(64);
        });

        b.Entity<MemberContextEligibilityRow>(e => { e.ToTable("member_context_eligibility_local"); e.HasKey(x => new { x.MemberId, x.ContextId }); e.Property(x => x.MemberId).HasMaxLength(64); e.Property(x => x.ContextId).HasMaxLength(64); });
        b.Entity<MemberRelationshipRow>(e => { e.ToTable("member_relationship_local"); e.HasKey(x => new { x.MemberId, x.OtherMemberId, x.ContextId }); e.Property(x => x.MemberId).HasMaxLength(64); e.Property(x => x.OtherMemberId).HasMaxLength(64); e.Property(x => x.ContextId).HasMaxLength(64); });

        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));

        foreach (var property in b.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (type == typeof(DateTimeOffset)) property.SetColumnType("timestamp with time zone");
            else if (type == typeof(bool)) property.SetColumnType("boolean");
        }
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

    private static byte[] SerializeVector(Vector vector)
    {
        var values = vector.ToArray();
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static Vector DeserializeVector(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0) throw new InvalidDataException("Invalid embedding payload length.");
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return new Vector(values);
    }
}

public sealed class PostgresTransactionGuardInterceptor : DbTransactionInterceptor
{
    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        // SET LOCAL is transaction-scoped and safe with PgBouncer transaction pooling.
        await using var command = connection.CreateCommand();
        command.Transaction = result;
        command.CommandText = "SET LOCAL lock_timeout = '3s'; SET LOCAL idle_in_transaction_session_timeout = '30s';";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
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
    public long RowVersion { get; set; }
}

public sealed class NlpEmbeddingRow
{
    public string IntentId { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public int Dimensions { get; set; }
    public string NormalizedHash { get; set; } = "";
    public Vector Embedding { get; set; } = new(Array.Empty<float>());
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
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
}

public sealed class NlpRankingConfigRow
{
    public string RankingVersion { get; set; } = "";
    public decimal SemanticWeight { get; set; }
    public decimal CategoryWeight { get; set; }
    public decimal IndustryWeight { get; set; }
    public decimal GeographyWeight { get; set; }
    public decimal FreshnessWeight { get; set; }
    public decimal EventWeight { get; set; }
    public decimal Threshold { get; set; }
    public string? ConfigJson { get; set; }
    public DateTimeOffset ActiveFrom { get; set; }
    public DateTimeOffset? ActiveTo { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
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
    public long RowVersion { get; set; }
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
    public decimal? RankingThreshold { get; set; }
    public int? CandidateCount { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long RowVersion { get; set; }
}

public sealed class NlpMatchResultRow
{
    public long MatchResultId { get; set; }
    public string RequestId { get; set; } = "";
    public string RequesterId { get; set; } = "";
    public string CandidateId { get; set; } = "";
    public short Rank { get; set; }
    public decimal SemanticScore { get; set; }
    public decimal? ReciprocalScore { get; set; }
    public decimal FinalScore { get; set; }
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
    public long RowVersion { get; set; }
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
