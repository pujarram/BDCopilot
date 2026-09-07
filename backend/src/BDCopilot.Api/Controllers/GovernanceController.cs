using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

/// <summary>Phase 8 — compliance checklist, version diff, and governance audit trail.</summary>
[ApiController]
[Route("api/governance")]
[Produces("application/json")]
public class GovernanceController : ControllerBase
{
    private readonly IGovernanceService _governance;

    public GovernanceController(IGovernanceService governance) => _governance = governance;

    [HttpGet("checklist/{generationId:guid}")]
    [ProducesResponseType(typeof(ExportComplianceChecklist), StatusCodes.Status200OK)]
    public ActionResult<ExportComplianceChecklist> GetChecklist(
        Guid generationId,
        [FromQuery] string documentTitle = "Draft")
        => Ok(_governance.GetDefaultChecklist(generationId, documentTitle));

    [HttpPost("checklist")]
    [ProducesResponseType(typeof(ComplianceChecklistResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ComplianceChecklistResult>> SubmitChecklist(
        [FromBody] SubmitComplianceChecklistRequest request,
        CancellationToken ct)
        => Ok(await _governance.SubmitChecklistAsync(request, ct));

    [HttpGet("checklist/{generationId:guid}/status")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> ChecklistStatus(Guid generationId, CancellationToken ct)
    {
        var allowed = await _governance.IsExportAllowedAsync(generationId, ct);
        return Ok(new { generationId, readyForExport = allowed });
    }

    [HttpPost("snapshots")]
    [ProducesResponseType(typeof(GenerationSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerationSnapshot>> SaveSnapshot(
        [FromBody] SaveGenerationSnapshotRequest request,
        CancellationToken ct)
        => Ok(await _governance.SaveSnapshotAsync(request, ct));

    [HttpGet("snapshots/{generationId:guid}")]
    [ProducesResponseType(typeof(List<GenerationSnapshot>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GenerationSnapshot>>> ListSnapshots(
        Guid generationId,
        CancellationToken ct)
        => Ok(await _governance.ListSnapshotsAsync(generationId, ct));

    [HttpGet("diff/{generationId:guid}")]
    [ProducesResponseType(typeof(GenerationVersionDiff), StatusCodes.Status200OK)]
    public async Task<ActionResult<GenerationVersionDiff>> Diff(
        Guid generationId,
        [FromQuery] int? fromVersion,
        [FromQuery] int? toVersion,
        CancellationToken ct)
        => Ok(await _governance.DiffVersionsAsync(generationId, fromVersion, toVersion, ct));

    [HttpGet("audit")]
    [ProducesResponseType(typeof(List<GovernanceAuditEvent>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GovernanceAuditEvent>>> Audit(
        [FromQuery] Guid? generationId,
        [FromQuery] string? eventType,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
        => Ok(await _governance.QueryAuditAsync(new GovernanceAuditQuery
        {
            GenerationId = generationId,
            EventType = eventType,
            Limit = limit
        }, ct));

    [HttpPost("audit")]
    public async Task<IActionResult> LogAudit([FromBody] GovernanceAuditEvent evt, CancellationToken ct)
    {
        await _governance.LogAuditAsync(evt, ct);
        return NoContent();
    }
}
