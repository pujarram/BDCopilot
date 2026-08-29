using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Api.Controllers;

/// <summary>Phase 4 admin console APIs — reindex, access audits, per-team token cost.</summary>
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IDocumentSyncService _sync;
    private readonly IAccessAuditService _audits;
    private readonly BdCopilotDbContext _db;

    public AdminController(IDocumentSyncService sync, IAccessAuditService audits, BdCopilotDbContext db)
    {
        _sync = sync;
        _audits = audits;
        _db = db;
    }

    [HttpPost("reindex")]
    public async Task<ActionResult<SyncHealthStatus>> Reindex([FromBody] ReindexRequest? request, CancellationToken ct)
    {
        // Full sync covers multi-site delta; optional site filter is applied via SyncSiteState.IsEnabled.
        if (!string.IsNullOrWhiteSpace(request?.SiteId))
        {
            var sites = await _db.SyncSiteStates.ToListAsync(ct);
            foreach (var site in sites)
            {
                site.IsEnabled = string.Equals(site.SiteId, request.SiteId, StringComparison.OrdinalIgnoreCase);
                // Force a full re-walk by clearing delta on the target site.
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
}
