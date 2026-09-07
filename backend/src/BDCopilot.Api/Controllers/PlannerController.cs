using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

/// <summary>Project Intelligence — Microsoft Planner snapshots and sync.</summary>
[ApiController]
[Route("api/planner")]
[Produces("application/json")]
public class PlannerController : ControllerBase
{
    private readonly IPlannerSyncService _planner;
    private readonly IProjectManagerService _projectManager;
    private readonly IPlannerSnapshotService _snapshots;
    private readonly IUnifiedIntelligenceService _unified;
    private readonly IPlannerCapacityService _capacity;
    private readonly IDeliveryCrossLinkService _crossLinks;
    private readonly IPipelineCapacityService _pipeline;

    public PlannerController(
        IPlannerSyncService planner,
        IProjectManagerService projectManager,
        IPlannerSnapshotService snapshots,
        IUnifiedIntelligenceService unified,
        IPlannerCapacityService capacity,
        IDeliveryCrossLinkService crossLinks,
        IPipelineCapacityService pipeline)
    {
        _planner = planner;
        _projectManager = projectManager;
        _snapshots = snapshots;
        _unified = unified;
        _capacity = capacity;
        _crossLinks = crossLinks;
        _pipeline = pipeline;
    }

    [HttpGet("health")]
    [ProducesResponseType(typeof(PlannerHealthSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlannerHealthSummary>> Health(CancellationToken ct)
        => Ok(await _planner.GetHealthSummaryAsync(ct));

    [HttpGet("plans")]
    [ProducesResponseType(typeof(List<PlannerPlanListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PlannerPlanListItem>>> Plans(CancellationToken ct)
        => Ok(await _planner.ListPlansAsync(ct));

    [HttpGet("tasks")]
    [ProducesResponseType(typeof(List<PlannerTaskListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PlannerTaskListItem>>> Tasks(
        [FromQuery] string? planId,
        [FromQuery] bool delayedOnly = false,
        [FromQuery] string? assignee = null,
        [FromQuery] DateTimeOffset? dueFrom = null,
        [FromQuery] DateTimeOffset? dueTo = null,
        CancellationToken ct = default)
        => Ok(await _planner.ListTasksAsync(planId, delayedOnly, assignee, dueFrom, dueTo, ct));

    [HttpGet("workload")]
    [ProducesResponseType(typeof(List<PlannerWorkloadRow>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PlannerWorkloadRow>>> Workload(CancellationToken ct)
        => Ok(await _planner.GetWorkloadAsync(ct));

    [HttpGet("insights")]
    [ProducesResponseType(typeof(ProjectManagerInsight), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectManagerInsight>> Insights(CancellationToken ct)
        => Ok(await _projectManager.GetInsightsAsync(ct));

    [HttpGet("report")]
    [ProducesResponseType(typeof(StakeholderWeeklyReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<StakeholderWeeklyReport>> Report(
        [FromQuery] bool useLlm = true,
        [FromQuery] string? userObjectId = null,
        CancellationToken ct = default)
        => Ok(await _projectManager.GenerateStakeholderReportAsync(useLlm, userObjectId, ct));

    [HttpGet("report.docx")]
    [Produces("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public async Task<IActionResult> ReportDocx(
        [FromQuery] bool useLlm = true,
        [FromQuery] string? userObjectId = null,
        CancellationToken ct = default)
    {
        var stream = await _projectManager.ExportStakeholderReportDocxAsync(useLlm, userObjectId, ct);
        return File(
            stream,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            $"bd-project-weekly-{DateTime.UtcNow:yyyyMMdd}.docx");
    }

    [HttpGet("burndown")]
    [ProducesResponseType(typeof(List<PlannerBurndownPoint>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PlannerBurndownPoint>>> Burndown(
        [FromQuery] int days = 30,
        CancellationToken ct = default)
        => Ok(await _snapshots.GetBurndownAsync(days, ct));

    [HttpPost("unified")]
    [ProducesResponseType(typeof(UnifiedIntelligenceResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnifiedIntelligenceResponse>> Unified(
        [FromBody] UnifiedIntelligenceRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserObjectId))
        {
            return BadRequest("userObjectId is required.");
        }

        return Ok(await _unified.QueryAsync(request, ct));
    }

    [HttpPost("sync")]
    [ProducesResponseType(typeof(PlannerSyncResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlannerSyncResult>> Sync(CancellationToken ct)
        => Ok(await _planner.SyncAsync(ct));

    [HttpGet("capacity")]
    [ProducesResponseType(typeof(PlannerCapacityHeatmap), StatusCodes.Status200OK)]
    public async Task<ActionResult<PlannerCapacityHeatmap>> Capacity(
        [FromQuery] int weeks = 6,
        CancellationToken ct = default)
        => Ok(await _capacity.GetHeatmapAsync(weeks, ct));

    [HttpGet("alerts/stalled")]
    [ProducesResponseType(typeof(List<PlannerStalledAlert>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PlannerStalledAlert>>> StalledAlerts(
        [FromQuery] int days = 7,
        CancellationToken ct = default)
        => Ok(await _snapshots.GetStalledAlertsAsync(days, ct));

    [HttpGet("cross-links")]
    [ProducesResponseType(typeof(DeliveryCrossLinkResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DeliveryCrossLinkResponse>> CrossLinks(
        [FromQuery] string userObjectId,
        [FromQuery] int maxTasks = 8,
        [FromQuery] bool refresh = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userObjectId))
        {
            return BadRequest("userObjectId is required.");
        }

        return Ok(await _crossLinks.GetCrossLinksAsync(userObjectId, maxTasks, refresh, ct));
    }

    [HttpPost("cross-links/feedback")]
    [ProducesResponseType(typeof(CrossLinkFeedbackResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<CrossLinkFeedbackResult>> CrossLinkFeedback(
        [FromBody] CrossLinkFeedbackRequest request,
        CancellationToken ct = default)
    {
        if (request.LinkId == Guid.Empty || string.IsNullOrWhiteSpace(request.UserObjectId))
        {
            return BadRequest("linkId and userObjectId are required.");
        }

        return Ok(await _crossLinks.SubmitFeedbackAsync(request, ct));
    }

    [HttpGet("pipeline-capacity")]
    [ProducesResponseType(typeof(PipelineCapacityView), StatusCodes.Status200OK)]
    public async Task<ActionResult<PipelineCapacityView>> PipelineCapacity(CancellationToken ct)
        => Ok(await _pipeline.GetPipelineCapacityAsync(ct));

    [HttpGet("alerts/pursuit-deadlines")]
    [ProducesResponseType(typeof(List<PursuitDeadlineAlert>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PursuitDeadlineAlert>>> PursuitDeadlines(
        [FromQuery] int withinDays = 14,
        CancellationToken ct = default)
        => Ok(await _pipeline.GetPursuitDeadlineAlertsAsync(withinDays, ct));

    [HttpGet("alerts/stale-documents")]
    [ProducesResponseType(typeof(List<StaleDocumentAlert>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<StaleDocumentAlert>>> StaleDocuments(CancellationToken ct)
        => Ok(await _pipeline.GetStaleDocumentAlertsAsync(ct));
}
