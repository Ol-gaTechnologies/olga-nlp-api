using Microsoft.EntityFrameworkCore;
using Olga.Nlp.Domain;

namespace Olga.Nlp.Infrastructure;

public sealed class NlpDbContext(DbContextOptions<NlpDbContext> options) : DbContext(options)
{
    public DbSet<NlpIntentRow> Intents => Set<NlpIntentRow>();
    public DbSet<NlpEmbeddingRow> Embeddings => Set<NlpEmbeddingRow>();
    public DbSet<NlpRankingConfigRow> RankingConfigs => Set<NlpRankingConfigRow>();
    public DbSet<NlpModelVersionRow> ModelVersions => Set<NlpModelVersionRow>();
    public DbSet<NlpMatchResultRow> MatchResults => Set<NlpMatchResultRow>();
    public DbSet<NlpFeedbackRow> Feedback => Set<NlpFeedbackRow>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("nlp");
        b.Entity<NlpIntentRow>(e => { e.ToTable("NlpIntent"); e.HasKey(x => x.IntentId); e.Property(x => x.IntentId).HasMaxLength(64); e.Property(x => x.MemberId).HasMaxLength(64).IsRequired(); e.Property(x => x.ContextId).HasMaxLength(64).IsRequired(); e.Property(x => x.OriginalText).HasMaxLength(4000).IsRequired(); e.Property(x => x.NormalizedText).HasMaxLength(4000).IsRequired(); e.HasIndex(x => new { x.ContextId, x.MemberId, x.Status, x.ExpiresAt }); });
        b.Entity<NlpMatchResultRow>(e => { e.ToTable("NlpMatchResult"); e.HasKey(x => x.Id); e.Property(x => x.RequestId).HasMaxLength(64).IsRequired(); e.Property(x => x.RequesterId).HasMaxLength(64).IsRequired(); e.Property(x => x.CandidateId).HasMaxLength(64).IsRequired(); e.HasIndex(x => new { x.RequestId, x.CandidateId }).IsUnique(); });
        b.Entity<NlpEmbeddingRow>(e => { e.ToTable("NlpEmbedding"); e.HasKey(x => new { x.IntentId, x.ModelVersion }); });
        b.Entity<NlpRankingConfigRow>(e => { e.ToTable("NlpRankingConfig"); e.HasKey(x => x.RankingVersion); });
        b.Entity<NlpModelVersionRow>(e => { e.ToTable("NlpModelVersion"); e.HasKey(x => x.ModelVersion); });
        b.Entity<NlpFeedbackRow>(e => { e.ToTable("NlpFeedback"); e.HasKey(x => x.Id); });
    }
}
public sealed class NlpIntentRow { public string IntentId { get; set; } = ""; public string MemberId { get; set; } = ""; public string ContextId { get; set; } = ""; public IntentType IntentType { get; set; } public string OriginalText { get; set; } = ""; public string NormalizedText { get; set; } = ""; public string Status { get; set; } = "ACTIVE"; public DateTimeOffset ExpiresAt { get; set; } }
public sealed class NlpMatchResultRow { public long Id { get; set; } public string RequestId { get; set; } = ""; public string RequesterId { get; set; } = ""; public string CandidateId { get; set; } = ""; public double Score { get; set; } public string ReasonCodes { get; set; } = ""; public string ReasonText { get; set; } = ""; public string ModelVersion { get; set; } = ""; public string PreprocessingVersion { get; set; } = ""; public string RankingVersion { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } }
public sealed class NlpEmbeddingRow { public string IntentId { get; set; } = ""; public string ModelVersion { get; set; } = ""; public int Dimensions { get; set; } public string NormalizedHash { get; set; } = ""; public byte[] Embedding { get; set; } = []; public DateTimeOffset CreatedAt { get; set; } }
public sealed class NlpRankingConfigRow { public string RankingVersion { get; set; } = ""; public string WeightsJson { get; set; } = ""; public decimal Threshold { get; set; } public DateTimeOffset ActiveFrom { get; set; } public DateTimeOffset? ActiveTo { get; set; } public int Version { get; set; } }
public sealed class NlpModelVersionRow { public string ModelVersion { get; set; } = ""; public string Provider { get; set; } = ""; public int Dimensions { get; set; } public string PreprocessingVersion { get; set; } = ""; public bool Active { get; set; } public DateTimeOffset CreatedAt { get; set; } }
public sealed class NlpFeedbackRow { public long Id { get; set; } public string RequestId { get; set; } = ""; public string RequesterId { get; set; } = ""; public string CandidateId { get; set; } = ""; public string Label { get; set; } = ""; public string? Reason { get; set; } public DateTimeOffset CreatedAt { get; set; } }
