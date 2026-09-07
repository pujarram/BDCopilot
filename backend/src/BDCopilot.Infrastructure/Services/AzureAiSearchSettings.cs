namespace BDCopilot.Infrastructure.Services;

public class AzureAiSearchSettings
{
    public const string SectionName = "AzureSearch";

    public string Endpoint { get; set; } = "";
    public string IndexName { get; set; } = "bdcopilot-chunks";
    public string ApiKey { get; set; } = "";
    public int DefaultTopK { get; set; } = 8;

    /// <summary>Index field storing tenant id for multi-tenant isolation.</summary>
    public string TenantIdField { get; set; } = "tenantId";

    /// <summary>Index field storing corpus source (Local, Online, Planner).</summary>
    public string CorpusSourceField { get; set; } = "corpusSource";

    /// <summary>Fallback tenant id when documents are not stamped (single-tenant pilots).</summary>
    public string DefaultTenantId { get; set; } = "";

    public bool EnableTenantFilter { get; set; } = true;
}
