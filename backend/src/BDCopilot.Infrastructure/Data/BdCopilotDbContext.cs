using BDCopilot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Data;

/// <summary>
/// EF Core context for the metadata + pgvector store described in the architecture doc.
/// Column names use snake_case to match <c>database/init.sql</c>.
/// </summary>
public class BdCopilotDbContext : DbContext
{
    private readonly int _embeddingDimensions;

    public BdCopilotDbContext(DbContextOptions<BdCopilotDbContext> options, int embeddingDimensions = 768)
        : base(options)
    {
        _embeddingDimensions = embeddingDimensions;
    }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<SyncSiteState> SyncSiteStates => Set<SyncSiteState>();
    public DbSet<GenerationFeedback> GenerationFeedback => Set<GenerationFeedback>();
    public DbSet<TokenUsageRecord> TokenUsageRecords => Set<TokenUsageRecord>();
    public DbSet<AccessAuditRecord> AccessAuditRecords => Set<AccessAuditRecord>();
    public DbSet<RfpDocument> RfpDocuments => Set<RfpDocument>();
    public DbSet<GeneratedDocumentHistory> GeneratedDocumentHistories => Set<GeneratedDocumentHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.HasKey(d => d.DocumentId);
            e.Property(d => d.DocumentId).HasColumnName("document_id");
            e.Property(d => d.FileName).HasColumnName("file_name").IsRequired().HasMaxLength(512);
            e.Property(d => d.FileType).HasColumnName("file_type").IsRequired().HasMaxLength(16);
            e.Property(d => d.Owner).HasColumnName("owner").HasMaxLength(256);
            e.Property(d => d.CreatedDate).HasColumnName("created_date");
            e.Property(d => d.ModifiedDate).HasColumnName("modified_date");
            e.Property(d => d.TeamsChannel).HasColumnName("teams_channel").IsRequired().HasMaxLength(256);
            e.Property(d => d.GraphDriveId).HasColumnName("graph_drive_id").HasMaxLength(512);
            e.Property(d => d.GraphDriveItemId).HasColumnName("graph_drive_item_id").HasMaxLength(512);
            e.Property(d => d.SharePointUrl).HasColumnName("share_point_url").IsRequired().HasMaxLength(2048);
            e.Property(d => d.AclHash).HasColumnName("acl_hash").HasMaxLength(128);
            e.Property(d => d.IndexStatus).HasColumnName("index_status").HasConversion<string>().HasMaxLength(32);
            e.Property(d => d.LastIndexedAt).HasColumnName("last_indexed_at");
            e.HasIndex(d => d.GraphDriveItemId);
            e.HasIndex(d => d.GraphDriveId);
            e.HasMany(d => d.Chunks)
                .WithOne(c => c.Document)
                .HasForeignKey(c => c.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.ToTable("document_chunks");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.DocumentId).HasColumnName("document_id");
            e.Property(c => c.ChunkIndex).HasColumnName("chunk_index");
            e.Property(c => c.Content).HasColumnName("content").IsRequired();
            e.Property(c => c.Locator).HasColumnName("locator").HasMaxLength(64);
            e.Property(c => c.Embedding).HasColumnName("embedding").HasColumnType($"vector({_embeddingDimensions})");
        });

        modelBuilder.Entity<DocumentChunk>()
            .HasIndex(c => c.Embedding)
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops");

        modelBuilder.Entity<SyncSiteState>(e =>
        {
            e.ToTable("sync_site_state");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasColumnName("id");
            e.Property(s => s.SiteId).HasColumnName("site_id").IsRequired().HasMaxLength(512);
            e.Property(s => s.SiteDisplayName).HasColumnName("site_display_name").HasMaxLength(512);
            e.Property(s => s.DeltaLink).HasColumnName("delta_link");
            e.Property(s => s.LastSuccessAt).HasColumnName("last_success_at");
            e.Property(s => s.LastAttemptAt).HasColumnName("last_attempt_at");
            e.Property(s => s.LastError).HasColumnName("last_error");
            e.Property(s => s.DocumentsIndexed).HasColumnName("documents_indexed");
            e.Property(s => s.IsEnabled).HasColumnName("is_enabled");
            e.HasIndex(s => s.SiteId).IsUnique();
        });

        modelBuilder.Entity<GenerationFeedback>(e =>
        {
            e.ToTable("generation_feedback");
            e.HasKey(f => f.Id);
            e.Property(f => f.Id).HasColumnName("id");
            e.Property(f => f.GenerationId).HasColumnName("generation_id");
            e.Property(f => f.UserObjectId).HasColumnName("user_object_id").IsRequired().HasMaxLength(256);
            e.Property(f => f.Action).HasColumnName("action").IsRequired().HasMaxLength(32);
            e.Property(f => f.SectionTitle).HasColumnName("section_title").HasMaxLength(512);
            e.Property(f => f.Notes).HasColumnName("notes");
            e.Property(f => f.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<TokenUsageRecord>(e =>
        {
            e.ToTable("token_usage");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.UserObjectId).HasColumnName("user_object_id").IsRequired().HasMaxLength(256);
            e.Property(t => t.TeamId).HasColumnName("team_id").HasMaxLength(256);
            e.Property(t => t.Initiative).HasColumnName("initiative").HasMaxLength(512);
            e.Property(t => t.Operation).HasColumnName("operation").IsRequired().HasMaxLength(32);
            e.Property(t => t.Provider).HasColumnName("provider").IsRequired().HasMaxLength(64);
            e.Property(t => t.Model).HasColumnName("model").IsRequired().HasMaxLength(128);
            e.Property(t => t.PromptTokens).HasColumnName("prompt_tokens");
            e.Property(t => t.CompletionTokens).HasColumnName("completion_tokens");
            e.Property(t => t.TotalTokens).HasColumnName("total_tokens");
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<AccessAuditRecord>(e =>
        {
            e.ToTable("access_audit");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.UserObjectId).HasColumnName("user_object_id").IsRequired().HasMaxLength(256);
            e.Property(a => a.DocumentId).HasColumnName("document_id");
            e.Property(a => a.Allowed).HasColumnName("allowed");
            e.Property(a => a.Reason).HasColumnName("reason").IsRequired().HasMaxLength(512);
            e.Property(a => a.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<RfpDocument>(e =>
        {
            e.ToTable("rfp_documents");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.GenerationId).HasColumnName("generation_id");
            e.Property(r => r.Title).HasColumnName("title").IsRequired().HasMaxLength(512);
            e.Property(r => r.Customer).HasColumnName("customer").IsRequired().HasMaxLength(256);
            e.Property(r => r.Tone).HasColumnName("tone").HasMaxLength(64);
            e.Property(r => r.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(r => r.CreatedByUserObjectId).HasColumnName("created_by_user_object_id").IsRequired().HasMaxLength(256);
            e.Property(r => r.CreatedByDisplayName).HasColumnName("created_by_display_name").HasMaxLength(256);
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at");
            e.Property(r => r.ExecutiveSummary).HasColumnName("executive_summary");
            e.Property(r => r.UnderstandingOfRequirements).HasColumnName("understanding_of_requirements");
            e.Property(r => r.ProposedSolutionArchitecture).HasColumnName("proposed_solution_architecture");
            e.Property(r => r.SecurityCompliance).HasColumnName("security_compliance");
            e.Property(r => r.DeliveryTimelineTeam).HasColumnName("delivery_timeline_team");
            e.Property(r => r.CommercialsPricing).HasColumnName("commercials_pricing");
            e.Property(r => r.ChannelSharePointUrl).HasColumnName("channel_share_point_url").HasMaxLength(2048);
            e.Property(r => r.ChannelUploadStatus).HasColumnName("channel_upload_status").HasMaxLength(64);
            e.Property(r => r.ChannelUploadError).HasColumnName("channel_upload_error");
            e.HasIndex(r => r.CreatedByUserObjectId);
            e.HasIndex(r => r.CreatedAt);
            e.HasIndex(r => r.GenerationId);
        });

        modelBuilder.Entity<GeneratedDocumentHistory>(e =>
        {
            e.ToTable("generated_document_history");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.GenerationId).HasColumnName("generation_id");
            e.Property(r => r.DocumentType).HasColumnName("document_type").IsRequired().HasMaxLength(64);
            e.Property(r => r.Title).HasColumnName("title").IsRequired().HasMaxLength(512);
            e.Property(r => r.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(r => r.MetadataJson).HasColumnName("metadata_json").HasColumnType("jsonb");
            e.Property(r => r.SectionsJson).HasColumnName("sections_json").HasColumnType("jsonb");
            e.Property(r => r.CreatedByUserObjectId).HasColumnName("created_by_user_object_id").IsRequired().HasMaxLength(256);
            e.Property(r => r.CreatedByDisplayName).HasColumnName("created_by_display_name").HasMaxLength(256);
            e.Property(r => r.CreatedAt).HasColumnName("created_at");
            e.Property(r => r.UpdatedAt).HasColumnName("updated_at");
            e.Property(r => r.ChannelSharePointUrl).HasColumnName("channel_share_point_url").HasMaxLength(2048);
            e.Property(r => r.ChannelUploadStatus).HasColumnName("channel_upload_status").HasMaxLength(64);
            e.Property(r => r.ChannelUploadError).HasColumnName("channel_upload_error");
            e.HasIndex(r => r.DocumentType);
            e.HasIndex(r => r.CreatedByUserObjectId);
            e.HasIndex(r => r.CreatedAt);
            e.HasIndex(r => r.GenerationId);
        });
    }
}
