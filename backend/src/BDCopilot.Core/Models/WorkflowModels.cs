namespace BDCopilot.Core.Models;

/// <summary>Per-site Graph delta cursor (Phase 1 pilot + Phase 4 multi-site).</summary>
public class SyncSiteState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string SiteId { get; set; }
    public string? SiteDisplayName { get; set; }
    public string? DeltaLink { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public int DocumentsIndexed { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class SyncHealthStatus
{
    public DateTimeOffset? LastSuccessAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
    public int DocumentsIndexed { get; set; }
    public int SitesConfigured { get; set; }
    public bool IsHealthy { get; set; }
    public string StatusMessage { get; set; } = "";
    public List<SyncSiteHealthItem> Sites { get; set; } = new();
}

public class SyncSiteHealthItem
{
    public required string SiteId { get; set; }
    public string? SiteDisplayName { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public int DocumentsIndexed { get; set; }
    public bool HasDeltaLink { get; set; }
}

/// <summary>Result of GET /api/sync/graph-probe — auth + site access without indexing.</summary>
public class GraphProbeResult
{
    public bool GraphConfigured { get; set; }
    public bool TokenAcquired { get; set; }
    public string? TenantId { get; set; }
    public string? ClientIdSuffix { get; set; }
    public string? PilotSitePath { get; set; }
    public string? ResolvedSiteId { get; set; }
    public string? ResolvedSiteName { get; set; }
    public string? ResolvedSiteUrl { get; set; }
    public int? DriveCount { get; set; }
    public bool RfpFolderFound { get; set; }
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public List<string> Steps { get; set; } = new();
}

/// <summary>Quiet accept/edit/discard signal for future voice tuning (Phase 3).</summary>
public class GenerationFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public required string Action { get; set; } // Accept | Edit | Discard | Approve | Export
    public string? SectionTitle { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TokenUsageRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserObjectId { get; set; }
    public string? TeamId { get; set; }
    public string? Initiative { get; set; }
    public required string Operation { get; set; } // Chat | Rfp | BusinessCase | Proposal | Embed
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
    /// <summary>Wall-clock duration of the AI call in milliseconds (cost &amp; latency dashboards).</summary>
    public int DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AccessAuditRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserObjectId { get; set; }
    public Guid DocumentId { get; set; }
    public bool Allowed { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ExportResult
{
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public required string DownloadUrl { get; set; }
    public required string Status { get; set; }
}

public class ApproveGenerationRequest
{
    public Guid GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public required GeneratedDocument Document { get; set; }
}

public class GenerationFeedbackRequest
{
    public Guid GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public required string Action { get; set; }
    public string? SectionTitle { get; set; }
    public string? Notes { get; set; }
}

public class ExportGenerationRequest
{
    public Guid GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public required string Format { get; set; } // docx | pptx | zip
    public required GeneratedDocument Document { get; set; }
    public bool RequireApproved { get; set; } = true;
}

public class TeamTokenCostRow
{
    public string? TeamId { get; set; }
    public string? Initiative { get; set; }
    public long TotalTokens { get; set; }
    public int RequestCount { get; set; }
}

public class ReindexRequest
{
    public string? SiteId { get; set; }
}
