using BDCopilot.Api.Filters;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using BDCopilot.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BDCopilot.Api.Controllers;

/// <summary>Phase 4 admin console APIs — reindex, access audits, per-team token cost.</summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[AdminAuthorize]
public class AdminController : ControllerBase
{
    private readonly IDocumentSyncService _sync;
    private readonly IAccessAuditService _audits;
    private readonly BdCopilotDbContext _db;
    private readonly IConfiguration _config;
    private readonly GraphClientFactory _graph;
    private readonly PlannerSyncSettings _planner;
    private readonly AzureAdSettings _azureAd;
    private readonly GraphSyncSettings _graphSettings;
    private readonly CustomerRolloutSettings _customer;
    private readonly ICapacityImportService _capacityImport;
    private readonly IPartnerOnboardingService _onboarding;

    public AdminController(
        IDocumentSyncService sync,
        IAccessAuditService audits,
        BdCopilotDbContext db,
        IConfiguration config,
        GraphClientFactory graph,
        IOptions<PlannerSyncSettings> planner,
        IOptions<AzureAdSettings> azureAd,
        IOptions<GraphSyncSettings> graphSettings,
        IOptions<CustomerRolloutSettings> customer,
        ICapacityImportService capacityImport,
        IPartnerOnboardingService onboarding)
    {
        _sync = sync;
        _audits = audits;
        _db = db;
        _config = config;
        _graph = graph;
        _planner = planner.Value;
        _azureAd = azureAd.Value;
        _graphSettings = graphSettings.Value;
        _customer = customer.Value;
        _capacityImport = capacityImport;
        _onboarding = onboarding;
    }

    [HttpPost("reindex")]
    public async Task<ActionResult<SyncHealthStatus>> Reindex([FromBody] ReindexRequest? request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request?.SiteId))
        {
            var sites = await _db.SyncSiteStates.ToListAsync(ct);
            foreach (var site in sites)
            {
                site.IsEnabled = string.Equals(site.SiteId, request.SiteId, StringComparison.OrdinalIgnoreCase);
                if (site.IsEnabled) site.DeltaLink = null;
            }
            await _db.SaveChangesAsync(ct);
        }

        await _sync.SyncAsync(ct);
        return Ok(await _sync.GetHealthAsync(ct));
    }

    [HttpGet("access-audits")]
    public async Task<ActionResult<IReadOnlyList<AccessAuditRecord>>> AccessAudits([FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await _audits.ListRecentAsync(limit, ct));

    [HttpGet("token-costs")]
    public async Task<ActionResult<List<TeamTokenCostRow>>> TokenCosts(CancellationToken ct)
    {
        var rows = await _db.TokenUsageRecords
            .GroupBy(t => new { t.TeamId, t.Initiative })
            .Select(g => new TeamTokenCostRow
            {
                TeamId = g.Key.TeamId,
                Initiative = g.Key.Initiative,
                TotalTokens = g.Sum(x => (long)x.TotalTokens),
                RequestCount = g.Count()
            })
            .OrderByDescending(r => r.TotalTokens)
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpGet("planner-status")]
    public ActionResult<PlannerLiveStatus> PlannerStatus()
        => Ok(BuildPlannerLiveStatus());

    [HttpGet("telemetry")]
    public async Task<ActionResult<AdminTelemetrySummary>> Telemetry(CancellationToken ct)
    {
        var since1h = DateTimeOffset.UtcNow.AddHours(-1);
        var since24h = DateTimeOffset.UtcNow.AddHours(-24);
        var appInsights = !string.IsNullOrWhiteSpace(_config["ApplicationInsights:ConnectionString"]);

        var top = await _db.TokenUsageRecords
            .GroupBy(t => new { t.TeamId, t.Initiative })
            .Select(g => new TeamTokenCostRow
            {
                TeamId = g.Key.TeamId,
                Initiative = g.Key.Initiative,
                TotalTokens = g.Sum(x => (long)x.TotalTokens),
                RequestCount = g.Count()
            })
            .OrderByDescending(r => r.TotalTokens)
            .Take(10)
            .ToListAsync(ct);

        var tokens24 = await _db.TokenUsageRecords
            .Where(t => t.CreatedAt >= since24h)
            .ToListAsync(ct);

        var latencies = tokens24.Where(t => t.DurationMs > 0).Select(t => (double)t.DurationMs).OrderBy(x => x).ToList();
        double avgLatency = latencies.Count == 0 ? 0 : latencies.Average();
        double p95 = 0;
        if (latencies.Count > 0)
        {
            var idx = (int)Math.Clamp(Math.Ceiling(latencies.Count * 0.95) - 1, 0, latencies.Count - 1);
            p95 = latencies[idx];
        }

        var plannerStatus = BuildPlannerLiveStatus();

        return Ok(new AdminTelemetrySummary
        {
            ApplicationInsightsConfigured = appInsights,
            AccessDenialsLastHour = await _db.AccessAuditRecords
                .CountAsync(a => !a.Allowed && a.CreatedAt >= since1h, ct),
            AccessDenialsLast24Hours = await _db.AccessAuditRecords
                .CountAsync(a => !a.Allowed && a.CreatedAt >= since24h, ct),
            TokensLast24Hours = tokens24.Sum(t => (long)t.TotalTokens),
            RequestsLast24Hours = tokens24.Count,
            AvgLatencyMsLast24Hours = Math.Round(avgLatency, 1),
            P95LatencyMsLast24Hours = Math.Round(p95, 1),
            TopTokenConsumers = top,
            SyncHealth = await _sync.GetHealthAsync(ct),
            PlannerLive = plannerStatus,
            Guidance = appInsights
                ? "App Insights is on. Use the Kusto hint below for cost & latency workbooks in Azure Monitor."
                : "Set ApplicationInsights:ConnectionString (App Service / Key Vault) to enable cloud telemetry.",
            AppInsightsKustoHint =
                """
                customEvents
                | where name == "BdCopilot.TokenUsage"
                | extend total=todouble(customMeasurements.TotalTokens), ms=todouble(customMeasurements.DurationMs)
                | summarize tokens=sum(total), avgMs=avg(ms), p95Ms=percentile(ms, 95), requests=count() by bin(timestamp, 1h)
                | render timechart
                """
        });
    }

    [HttpGet("cutover-status")]
    public async Task<ActionResult<TenantCutoverStatus>> CutoverStatus(CancellationToken ct)
    {
        var since24h = DateTimeOffset.UtcNow.AddHours(-24);
        var keyVaultUri = _config["KeyVault:Uri"] ?? Environment.GetEnvironmentVariable("KEYVAULT_URI");
        var keyVaultConfigured = !string.IsNullOrWhiteSpace(keyVaultUri);

        var entraConfigured = !string.IsNullOrWhiteSpace(_azureAd.TenantId)
                              && !string.IsNullOrWhiteSpace(_azureAd.ClientId);
        var spaConfigured = !string.IsNullOrWhiteSpace(_azureAd.SpaClientId)
                            || entraConfigured;
        var appInsights = !string.IsNullOrWhiteSpace(_config["ApplicationInsights:ConnectionString"]);
        var postgres = !string.IsNullOrWhiteSpace(_config.GetConnectionString("Postgres"));
        var plannerStatus = BuildPlannerLiveStatus();

        var siteIds = _graphSettings.SiteIds ?? [];
        var sharePointSiteCount = siteIds.Count(id => !string.IsNullOrWhiteSpace(id))
                                  + (string.IsNullOrWhiteSpace(_graphSettings.PilotSiteId) ? 0 : 1)
                                  + (string.IsNullOrWhiteSpace(_graphSettings.PilotSitePath) ? 0 : 1);
        var sharePointConfigured = _graph.IsConfigured
            && sharePointSiteCount > 0;

        var kvAndGraph = keyVaultConfigured && _graph.IsConfigured;

        var denials24h = await _db.AccessAuditRecords
            .CountAsync(a => !a.Allowed && a.CreatedAt >= since24h, ct);
        var audits24h = await _db.AccessAuditRecords
            .CountAsync(a => a.CreatedAt >= since24h, ct);

        var aclAuditing = _azureAd.EnforceAcl && (_graph.IsConfigured || audits24h > 0);

        var items = new List<TenantCutoverItem>
        {
            Item("kv-graph", "Key Vault + Graph credentials", kvAndGraph,
                kvAndGraph
                    ? $"Secrets from {keyVaultUri}; Graph app-only client configured"
                    : "Set KEYVAULT_URI and Graph--TenantId/ClientId/ClientSecret in Key Vault",
                "docs/azure/Setup-KeyVaultSecrets.ps1"),
            Item("graph-consent", "Graph admin consent (Tasks.Read.All, Group.Read.All)", _graph.IsConfigured,
                "Run Grant-GraphPlannerConsent.ps1 after adding app permissions in Entra",
                "docs/azure/Grant-GraphPlannerConsent.ps1"),
            Item("planner-ids", "Planner GroupIds / PlanIds (live sync)", plannerStatus.IsLiveConfigured,
                plannerStatus.Guidance,
                "docs/azure/customers/README.md"),
            Item("sharepoint-sites", "SharePoint sites configured (PilotSitePath / SiteIds)", sharePointConfigured,
                sharePointConfigured
                    ? $"{sharePointSiteCount} site(s) · path {_graphSettings.PilotSitePath ?? "—"}"
                    : "Set Graph--PilotSitePath and/or Graph--SiteIds--N in Key Vault",
                "docs/azure/customers/README.md"),
            Item("entra-api", "Entra API app (TenantId, ClientId, Audience)", entraConfigured,
                entraConfigured ? "JWT validation enabled" : "Set AzureAd:TenantId and AzureAd:ClientId",
                "docs/PRODUCTION_HARDENING.md#2-entra-sso-msal--api-jwt"),
            Item("entra-spa", "Entra SPA client (SpaClientId, redirect URIs)", spaConfigured,
                spaConfigured ? "MSAL Sign in with Microsoft enabled" : "Set AzureAd:SpaClientId for Angular",
                "docs/PRODUCTION_HARDENING.md#2-entra-sso-msal--api-jwt"),
            Item("admin-role", "BdCopilot.Admin role assigned to operators",
                User.IsInRole("BdCopilot.Admin")
                || (_azureAd.AllowPilotAdminLogin && !entraConfigured),
                User.IsInRole("BdCopilot.Admin")
                    ? "Current caller has BdCopilot.Admin"
                    : (_azureAd.AllowPilotAdminLogin && !entraConfigured
                        ? "Pilot mode — assign BdCopilot.Admin before SSO cutover"
                        : "Assign app role via Assign-BdCopilotAdminRole.ps1"),
                "docs/azure/Assign-BdCopilotAdminRole.ps1"),
            Item("appinsights", "Application Insights connection string", appInsights,
                appInsights ? "Telemetry active" : "Set ApplicationInsights-ConnectionString in Key Vault",
                "docs/azure/Setup-KeyVaultSecrets.ps1"),
            Item("postgres", "PostgreSQL connection string", postgres,
                postgres ? "Database reachable" : "Set ConnectionStrings-Postgres in Key Vault",
                "docs/azure/Setup-KeyVaultSecrets.ps1"),
            Item("enforce-acl", "ACL enforcement (EnforceAcl=true)", _azureAd.EnforceAcl && _graph.IsConfigured,
                _azureAd.EnforceAcl
                    ? (_graph.IsConfigured ? "Live Graph ACL checks enabled" : "EnforceAcl on but Graph not configured — SharePoint denies")
                    : "Set AzureAd:EnforceAcl=true after Graph is live",
                "docs/PRODUCTION_HARDENING.md#4-acl-cutover"),
            Item("require-auth", "API auth required (RequireAuthOnApi=true)", _azureAd.RequireAuthOnApi && entraConfigured,
                _azureAd.RequireAuthOnApi ? "All API routes require JWT" : "Enable after SSO cutover",
                null),
            Item("pilot-off", "Pilot admin login disabled", !_azureAd.AllowPilotAdminLogin,
                _azureAd.AllowPilotAdminLogin
                    ? "AllowPilotAdminLogin still true — disable for production"
                    : "SSO-only login",
                null),
            Item("monitor-wb", "Azure Monitor workbook published", appInsights,
                "Import docs/azure/BDCopilot-Monitor.workbook.json via Import-MonitorWorkbook.ps1",
                "docs/azure/Import-MonitorWorkbook.ps1", goLiveRequired: false),
            Item("acl-audits", "ACL audit trail active (24h)", audits24h > 0 || (_azureAd.EnforceAcl && _graph.IsConfigured),
                audits24h > 0
                    ? $"{audits24h} audit entries in 24h ({denials24h} denials) — review Admin Console table below"
                    : "Run retrieval/chat after EnforceAcl to populate access_audit",
                null, goLiveRequired: false)
        };

        var goLiveItems = items.Where(i => i.GoLiveRequired).ToList();
        var completed = goLiveItems.Count(i => i.Complete);
        var pendingIds = goLiveItems.Where(i => !i.Complete).Select(i => i.Id).ToList();
        var coreReady = pendingIds.Count == 0;
        var goLiveReady = coreReady;

        var tenantProfile = new CustomerTenantProfile
        {
            TenantId = string.IsNullOrWhiteSpace(_azureAd.TenantId) ? _graphSettings.TenantId : _azureAd.TenantId,
            PlannerGroupIds = (_planner.GroupIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            PlannerPlanIds = (_planner.PlanIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            PilotSitePath = _graphSettings.PilotSitePath,
            PilotSiteId = _graphSettings.PilotSiteId,
            SharePointSiteIds = siteIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            SyncFolderPaths = (_graphSettings.SyncFolderPaths ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList()
        };

        return Ok(new TenantCutoverStatus
        {
            ReadyForProduction = coreReady,
            GoLiveReady = goLiveReady,
            CompletedCount = completed,
            TotalCount = goLiveItems.Count,
            PendingItemIds = pendingIds,
            CustomerCode = string.IsNullOrWhiteSpace(_customer.Code) ? null : _customer.Code,
            CustomerDisplayName = string.IsNullOrWhiteSpace(_customer.DisplayName) ? null : _customer.DisplayName,
            CustomerProfilePath = string.IsNullOrWhiteSpace(_customer.ProfilePath) ? null : _customer.ProfilePath,
            SharePointConfigured = sharePointConfigured,
            SharePointSiteCount = sharePointSiteCount,
            KeyVaultConfigured = keyVaultConfigured,
            KeyVaultUri = keyVaultUri,
            Items = items,
            PlannerLive = plannerStatus,
            TenantProfile = tenantProfile,
            AccessDenialsLast24Hours = denials24h,
            AccessAuditsLast24Hours = audits24h,
            AclVerificationGuidance = denials24h > 0
                ? $"ACL is logging denials ({denials24h} in 24h). Review Recent access audits — denied rows confirm trimming works."
                : audits24h > 0
                    ? "Audits are flowing; no denials in 24h (users may only hit allowed docs, or EnforceAcl is off)."
                    : "After EnforceAcl cutover, exercise chat/search as a non-owner user and confirm deny rows appear when expected.",
            GoLiveGuidance = goLiveReady
                ? "All checklist items complete — cleared for go-live demo."
                : $"Complete {pendingIds.Count} pending item(s) before go-live: {string.Join(", ", pendingIds)}. Run docs/azure/Test-CutoverChecklist.ps1"
        });
    }

    private static TenantCutoverItem Item(
        string id, string label, bool complete, string detail, string? action, bool goLiveRequired = true) =>
        new()
        {
            Id = id,
            Label = label,
            Complete = complete,
            Status = complete ? "complete" : "pending",
            Detail = detail,
            Action = action,
            GoLiveRequired = goLiveRequired
        };

    [HttpGet("slo")]
    public async Task<IActionResult> Slo(CancellationToken ct)
    {
        var canConnect = await _db.Database.CanConnectAsync(ct);
        var health = await _sync.GetHealthAsync(ct);
        var deniedLastHour = await _db.AccessAuditRecords
            .CountAsync(a => !a.Allowed && a.CreatedAt > DateTimeOffset.UtcNow.AddHours(-1), ct);

        return Ok(new
        {
            database = canConnect ? "up" : "down",
            syncHealthy = health.IsHealthy,
            syncLastSuccessAt = health.LastSuccessAt,
            accessDenialsLastHour = deniedLastHour,
            slo = new
            {
                apiAvailabilityTarget = 0.995,
                syncFreshnessMinutesTarget = 15,
                zeroUnauthorizedCitations = true
            }
        });
    }

    [HttpPost("capacity/import")]
    [ProducesResponseType(typeof(CapacityImportResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<CapacityImportResult>> ImportCapacity(
        IFormFile file,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("CSV file is required.");
        }

        await using var stream = file.OpenReadStream();
        return Ok(await _capacityImport.ImportCsvAsync(stream, ct));
    }

    [HttpGet("onboarding")]
    [ProducesResponseType(typeof(PartnerOnboardingProfile), StatusCodes.Status200OK)]
    public async Task<ActionResult<PartnerOnboardingProfile>> GetOnboarding(CancellationToken ct)
        => Ok(await _onboarding.GetProfileAsync(ct));

    [HttpPost("onboarding")]
    [ProducesResponseType(typeof(PartnerOnboardingProfile), StatusCodes.Status200OK)]
    public async Task<ActionResult<PartnerOnboardingProfile>> SaveOnboarding(
        [FromBody] PartnerOnboardingProfile profile,
        CancellationToken ct = default)
        => Ok(await _onboarding.SaveProfileAsync(profile, ct));

    [HttpGet("onboarding/teams-manifest")]
    [Produces("application/json")]
    public async Task<IActionResult> TeamsManifest(CancellationToken ct)
    {
        var manifest = await _onboarding.GenerateTeamsManifestAsync(ct);
        return File(System.Text.Encoding.UTF8.GetBytes(manifest), "application/json", "bd-copilot-manifest.json");
    }

    private PlannerLiveStatus BuildPlannerLiveStatus()
    {
        var groupCount = (_planner.GroupIds ?? []).Count(id => !string.IsNullOrWhiteSpace(id));
        var planCount = (_planner.PlanIds ?? []).Count(id => !string.IsNullOrWhiteSpace(id));
        var live = _graph.IsConfigured && (groupCount > 0 || planCount > 0) && !_planner.SeedDemoData;

        return new PlannerLiveStatus
        {
            Enabled = _planner.Enabled,
            SeedDemoData = _planner.SeedDemoData,
            GraphConfigured = _graph.IsConfigured,
            GroupIdCount = groupCount,
            PlanIdCount = planCount,
            IsLiveConfigured = live,
            Guidance = live
                ? "Live Graph Planner sync is configured. Hangfire refreshes plans every 15 minutes."
                : "Grant app permissions Tasks.Read.All + Group.Read.All (admin consent), set Graph credentials, " +
                  "set Planner:GroupIds and/or Planner:PlanIds, and set Planner:SeedDemoData=false."
        };
    }
}
