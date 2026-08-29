using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/sync")]
[Produces("application/json")]
public class SyncController : ControllerBase
{
    private readonly IDocumentSyncService _sync;
    private readonly ILocalDocumentSyncService _localSync;

    public SyncController(IDocumentSyncService sync, ILocalDocumentSyncService localSync)
    {
        _sync = sync;
        _localSync = localSync;
    }

    /// <summary>Sync health for Document Library trust surface (last success, errors, sites).</summary>
    [HttpGet("health")]
    [ProducesResponseType(typeof(SyncHealthStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncHealthStatus>> Health(CancellationToken ct)
        => Ok(await _sync.GetHealthAsync(ct));

    /// <summary>Diagnose Graph app-only access to BDTeam without indexing.</summary>
    [HttpGet("graph-probe")]
    [ProducesResponseType(typeof(GraphProbeResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<GraphProbeResult>> GraphProbe(CancellationToken ct)
        => Ok(await _sync.ProbeGraphAsync(ct));

    /// <summary>Trigger an immediate sync (Hangfire also runs every 5 minutes).</summary>
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        try
        {
            await _sync.SyncAsync(ct);
            return Accepted(await _sync.GetHealthAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "SharePoint sync failed",
                Detail = ex.Message
            });
        }
    }

    /// <summary>Seed three pilot documents when Graph is not configured (local Phase 1 demo).</summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        await _sync.SeedPilotDocumentsAsync(ct);
        return Ok(await _sync.GetHealthAsync(ct));
    }

    /// <summary>Index files from the LocalDocs root (repo docs/ folder by default).</summary>
    [HttpPost("local-docs")]
    [ProducesResponseType(typeof(LocalDocsSyncResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<LocalDocsSyncResult>> SyncLocalDocs(CancellationToken ct)
        => Ok(await _localSync.SyncAsync(ct));
}
