namespace BDCopilot.Core.Models;

public class DashboardSummary
{
    public SyncHealthStatus? SyncHealth { get; set; }
    public PlannerHealthSummary? PlannerHealth { get; set; }
    public ProjectManagerInsight? ProjectInsights { get; set; }
    public int DocumentsIndexed { get; set; }
    public int AccessDenialsLast24Hours { get; set; }
    public long TokensLast24Hours { get; set; }
    public int RequestsLast24Hours { get; set; }
    public DashboardAiProvider AiProvider { get; set; } = new();
    public ProductionPosture ProductionPosture { get; set; } = new();
    public List<DashboardQuickLink> QuickLinks { get; set; } = [];
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class DashboardQuickLink
{
    public required string Label { get; set; }
    public required string Path { get; set; }
    public required string Description { get; set; }
}

public class DashboardAiProvider
{
    public string Provider { get; set; } = "Ollama";
    public string ChatModel { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
}

public class ProductionPosture
{
    public bool EntraConfigured { get; set; }
    public bool EnforceAcl { get; set; }
    public bool RequireAuthOnApi { get; set; }
    public bool GraphConfigured { get; set; }
    public bool PlannerLiveConfigured { get; set; }
    public bool ApplicationInsightsConfigured { get; set; }
    public bool TeamsBotEnabled { get; set; }
    public string AiProvider { get; set; } = "Ollama";
}

public class AdminTelemetrySummary
{
    public bool ApplicationInsightsConfigured { get; set; }
    public int AccessDenialsLastHour { get; set; }
    public int AccessDenialsLast24Hours { get; set; }
    public long TokensLast24Hours { get; set; }
    public int RequestsLast24Hours { get; set; }
    public double AvgLatencyMsLast24Hours { get; set; }
    public double P95LatencyMsLast24Hours { get; set; }
    public List<TeamTokenCostRow> TopTokenConsumers { get; set; } = [];
    public SyncHealthStatus? SyncHealth { get; set; }
    public PlannerLiveStatus? PlannerLive { get; set; }
    public string Guidance { get; set; } = "";
    public string AppInsightsKustoHint { get; set; } = "";
}

public class PlannerLiveStatus
{
    public bool Enabled { get; set; }
    public bool SeedDemoData { get; set; }
    public bool GraphConfigured { get; set; }
    public int GroupIdCount { get; set; }
    public int PlanIdCount { get; set; }
    public bool IsLiveConfigured { get; set; }
    public string Guidance { get; set; } = "";
}

public class TenantCutoverItem
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public bool Complete { get; set; }
    public string Status { get; set; } = "pending";
    public string Detail { get; set; } = "";
    public string? Action { get; set; }
    /// <summary>When false, item is post-go-live verification (not counted in 12/12 gate).</summary>
    public bool GoLiveRequired { get; set; } = true;
}

public class TenantCutoverStatus
{
    public bool ReadyForProduction { get; set; }
    /// <summary>True when all checklist items (typically 12/12) are complete — required before go-live demo.</summary>
    public bool GoLiveReady { get; set; }
    public int CompletedCount { get; set; }
    public int TotalCount { get; set; }
    public List<string> PendingItemIds { get; set; } = [];
    public string? CustomerCode { get; set; }
    public string? CustomerDisplayName { get; set; }
    public string? CustomerProfilePath { get; set; }
    public bool SharePointConfigured { get; set; }
    public int SharePointSiteCount { get; set; }
    public bool KeyVaultConfigured { get; set; }
    public string? KeyVaultUri { get; set; }
    public string WorkbookImportPath { get; set; } = "docs/azure/BDCopilot-Monitor.workbook.json";
    public string WorkbookImportScript { get; set; } = "docs/azure/Import-MonitorWorkbook.ps1";
    public string CutoverScript { get; set; } = "docs/azure/Invoke-CustomerCutover.ps1";
    public string ValidateScript { get; set; } = "docs/azure/Test-CutoverChecklist.ps1";
    public List<TenantCutoverItem> Items { get; set; } = [];
    public PlannerLiveStatus? PlannerLive { get; set; }
    public CustomerTenantProfile? TenantProfile { get; set; }
    public int AccessDenialsLast24Hours { get; set; }
    public int AccessAuditsLast24Hours { get; set; }
    public string AclVerificationGuidance { get; set; } = "";
    public string GoLiveGuidance { get; set; } = "";
}

/// <summary>Customer-specific ids documented for rollout (from live configuration).</summary>
public class CustomerTenantProfile
{
    public string? TenantId { get; set; }
    public List<string> PlannerGroupIds { get; set; } = [];
    public List<string> PlannerPlanIds { get; set; } = [];
    public string? PilotSitePath { get; set; }
    public string? PilotSiteId { get; set; }
    public List<string> SharePointSiteIds { get; set; } = [];
    public List<string> SyncFolderPaths { get; set; } = [];
}
