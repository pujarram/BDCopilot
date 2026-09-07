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

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
[AdminAuthorize]
public class DashboardController : ControllerBase
{
    private readonly IDocumentSyncService _sync;
    private readonly IPlannerSyncService _planner;
    private readonly IProjectManagerService _projectManager;
    private readonly BdCopilotDbContext _db;
    private readonly AiSettings _ai;
    private readonly AzureAdSettings _azureAd;
    private readonly GraphClientFactory _graph;
    private readonly PlannerSyncSettings _plannerSettings;
    private readonly TeamsBotSettings _teamsBot;
    private readonly IConfiguration _config;

    public DashboardController(
        IDocumentSyncService sync,
        IPlannerSyncService planner,
        IProjectManagerService projectManager,
        BdCopilotDbContext db,
        IOptions<AiSettings> ai,
        IOptions<AzureAdSettings> azureAd,
        GraphClientFactory graph,
        IOptions<PlannerSyncSettings> plannerSettings,
        IOptions<TeamsBotSettings> teamsBot,
        IConfiguration config)
    {
        _sync = sync;
        _planner = planner;
        _projectManager = projectManager;
        _db = db;
        _ai = ai.Value;
        _azureAd = azureAd.Value;
        _graph = graph;
        _plannerSettings = plannerSettings.Value;
        _teamsBot = teamsBot.Value;
        _config = config;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardSummary>> Summary(CancellationToken ct)
    {
        var since24h = DateTimeOffset.UtcNow.AddHours(-24);
        var syncHealth = await _sync.GetHealthAsync(ct);
        var plannerHealth = await _planner.GetHealthSummaryAsync(ct);
        var insights = await _projectManager.GetInsightsAsync(ct);
        var docCount = await _db.Documents.CountAsync(ct);
        var denials24h = await _db.AccessAuditRecords.CountAsync(
            a => !a.Allowed && a.CreatedAt >= since24h, ct);
        var tokenRows = await _db.TokenUsageRecords
            .Where(t => t.CreatedAt >= since24h)
            .ToListAsync(ct);

        var entraConfigured = !string.IsNullOrWhiteSpace(_azureAd.TenantId)
                              && !string.IsNullOrWhiteSpace(_azureAd.ClientId);
        var plannerLive = _plannerSettings.GroupIds.Any(id => !string.IsNullOrWhiteSpace(id))
                          || _plannerSettings.PlanIds.Any(id => !string.IsNullOrWhiteSpace(id));

        return Ok(new DashboardSummary
        {
            SyncHealth = syncHealth,
            PlannerHealth = plannerHealth,
            ProjectInsights = insights,
            DocumentsIndexed = docCount,
            AccessDenialsLast24Hours = denials24h,
            TokensLast24Hours = tokenRows.Sum(t => (long)t.TotalTokens),
            RequestsLast24Hours = tokenRows.Count,
            AiProvider = BuildAiStatus(),
            ProductionPosture = new ProductionPosture
            {
                EntraConfigured = entraConfigured,
                EnforceAcl = _azureAd.EnforceAcl,
                RequireAuthOnApi = _azureAd.RequireAuthOnApi,
                GraphConfigured = _graph.IsConfigured,
                PlannerLiveConfigured = plannerLive && !_plannerSettings.SeedDemoData,
                ApplicationInsightsConfigured = !string.IsNullOrWhiteSpace(
                    _config["ApplicationInsights:ConnectionString"]),
                TeamsBotEnabled = _teamsBot.Enabled,
                AiProvider = _ai.Provider
            },
            QuickLinks =
            [
                new DashboardQuickLink
                {
                    Label = "AI Chat",
                    Path = "/chat",
                    Description = "Grounded Q&A with SharePoint citations"
                },
                new DashboardQuickLink
                {
                    Label = "Find & reuse",
                    Path = "/search",
                    Description = "Search indexed BD material and reuse in generators"
                },
                new DashboardQuickLink
                {
                    Label = "RFP Generator",
                    Path = "/rfp",
                    Description = "Draft and export RFP responses"
                },
                new DashboardQuickLink
                {
                    Label = "Project Intelligence",
                    Path = "/projects",
                    Description = "Planner health, Gantt, AI PM, unified briefs"
                },
                new DashboardQuickLink
                {
                    Label = "Document Library",
                    Path = "/library",
                    Description = "Corpus sync status and indexed files"
                },
                new DashboardQuickLink
                {
                    Label = "Pursuits",
                    Path = "/pursuits",
                    Description = "Opportunity tracker, win/loss, linked docs"
                },
                new DashboardQuickLink
                {
                    Label = "ROI & usage",
                    Path = "/analytics",
                    Description = "Time saved, win-rate trend, customer AI cost"
                },
                new DashboardQuickLink
                {
                    Label = "Admin Console",
                    Path = "/admin",
                    Description = "Reindex, audits, token costs, telemetry"
                }
            ]
        });
    }

    private DashboardAiProvider BuildAiStatus() =>
        new()
        {
            Provider = _ai.Provider,
            ChatModel = _ai.Provider == "AzureOpenAI"
                ? _ai.AzureOpenAI.ChatDeployment
                : _ai.Ollama.ChatModel,
            EmbeddingModel = _ai.Provider == "AzureOpenAI"
                ? _ai.AzureOpenAI.EmbeddingDeployment
                : _ai.Ollama.EmbeddingModel
        };
}
