namespace BDCopilot.Core.Models;

/// <summary>Persisted cross-link between a delayed Planner task and a document chunk.</summary>
public class DeliveryCrossLinkRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid DocumentId { get; set; }
    public string LinkKind { get; set; } = "DeliveryDoc";
    public double Score { get; set; }
    public string? Locator { get; set; }
    public string? Excerpt { get; set; }
    public string? Rationale { get; set; }
    public DateTimeOffset ComputedAt { get; set; } = DateTimeOffset.UtcNow;
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
}

public class CrossLinkFeedbackRequest
{
    public required Guid LinkId { get; set; }
    public required string UserObjectId { get; set; }
    /// <summary>Upvote | Downvote | Dismiss | Pin</summary>
    public required string Action { get; set; }
}

public class CrossLinkFeedbackResult
{
    public Guid LinkId { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public double AdjustedScore { get; set; }
}

/// <summary>Cached Graph user display names for Planner assignee resolution.</summary>
public class PlannerUserCacheEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ObjectId { get; set; }
    public required string DisplayName { get; set; }
    public string? Mail { get; set; }
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>HR / Excel imported weekly capacity baseline per assignee.</summary>
public class PlannerCapacityOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string AssigneeKey { get; set; }
    public DateOnly WeekStart { get; set; }
    public double CapacityHours { get; set; } = 40;
    public string Source { get; set; } = "import";
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CapacityImportResult
{
    public int RowsProcessed { get; set; }
    public int OverridesUpserted { get; set; }
    public int TaskHoursUpdated { get; set; }
    public int Skipped { get; set; }
    public List<string> Warnings { get; set; } = [];
    public string Summary { get; set; } = "";
}

public class PartnerOnboardingProfile
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string SubscriptionId { get; set; } = "";
    public string ResourceGroup { get; set; } = "";
    public string KeyVaultName { get; set; } = "";
    public string AppServiceName { get; set; } = "";
    public List<string> PlannerGroupIds { get; set; } = [];
    public string PilotSitePath { get; set; } = "";
    public string GraphAppId { get; set; } = "";
    public string ApiAppId { get; set; } = "";
    public string SpaAppId { get; set; } = "";
    public string TeamsManifestBaseUrl { get; set; } = "";
    public int CutoverCompletedCount { get; set; }
    public int CutoverTotalCount { get; set; }
    public bool GoLiveReady { get; set; }
}

public class CompliancePackSettings
{
    public const string SectionName = "CompliancePacks";

    public string DefaultRegion { get; set; } = "EU";
    public Dictionary<string, ComplianceRegionPack> Regions { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EU"] = new ComplianceRegionPack
        {
            Label = "EU / GDPR",
            PromptSuffix = "Apply GDPR-aware language: data residency in EU, DPA references, lawful basis for processing.",
            ExportLanguage = "en-GB"
        },
        ["US"] = new ComplianceRegionPack
        {
            Label = "US",
            PromptSuffix = "Use US commercial terms; reference SOC 2 / NIST where security sections apply.",
            ExportLanguage = "en-US"
        },
        ["FI"] = new ComplianceRegionPack
        {
            Label = "Finland",
            PromptSuffix = "Draft in Finnish business tone where requested; cite Finnish regulatory context when relevant.",
            ExportLanguage = "fi-FI"
        }
    };
}

public class ComplianceRegionPack
{
    public string Label { get; set; } = "";
    public string PromptSuffix { get; set; } = "";
    public string ExportLanguage { get; set; } = "en-US";
}
