namespace BDCopilot.Infrastructure.Services;

public class GraphSyncSettings
{
    public const string SectionName = "Graph";

    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";

    /// <summary>SharePoint site id for the pilot site (Phase 1).</summary>
    public string PilotSiteId { get; set; } = "";

    /// <summary>
    /// Optional hostname:path lookup for the pilot site, e.g.
    /// <c>ikione.sharepoint.com:/sites/BDTeam</c>. Preferred over composite ids
    /// because Graph SDK can fail on comma-separated site ids.
    /// </summary>
    public string PilotSitePath { get; set; } = "";

    /// <summary>Optional list of additional site ids for Phase 4 multi-site sync.</summary>
    public List<string> SiteIds { get; set; } = new();

    /// <summary>
    /// When Graph is not configured, allow <see cref="DocumentSyncService.SeedPilotDocumentsAsync"/>
    /// to populate demo documents for local development.
    /// </summary>
    public bool AllowDevSeedWithoutGraph { get; set; } = true;

    /// <summary>
    /// When <c>AzureAd:EnforceAcl</c> is false, allow all authenticated users to read indexed
    /// documents without a live Graph permission check.
    /// </summary>
    public bool AllowDevBypass { get; set; } = true;

    /// <summary>Drive id for the BD Teams channel document library (Save to BD channel).</summary>
    public string BdChannelDriveId { get; set; } = "";

    /// <summary>Folder path under the drive for RFP uploads, e.g. "RFP" or "Shared Documents/RFP".</summary>
    public string RfpFolderPath { get; set; } = "RFP";

    /// <summary>Folder for Business Case channel uploads.</summary>
    public string BusinessCaseFolderPath { get; set; } = "Business Cases";

    /// <summary>Folder for Proposal channel uploads.</summary>
    public string ProposalFolderPath { get; set; } = "Proposals";

    /// <summary>
    /// Optional folder prefixes under the document library root (not including "Shared Documents").
    /// When non-empty, only files under these folders are indexed. Empty = entire drive(s).
    /// Example for WealthBD RFPs: <c>["General/RFP DataBase/IWM"]</c>.
    /// </summary>
    public List<string> SyncFolderPaths { get; set; } = new();
}
