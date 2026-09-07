using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/generate")]
[Produces("application/json")]
public class WorkflowController : ControllerBase
{
    private readonly IGenerationWorkflowService _workflow;
    private readonly IMultiApprovalService _multiApproval;

    public WorkflowController(IGenerationWorkflowService workflow, IMultiApprovalService multiApproval)
    {
        _workflow = workflow;
        _multiApproval = multiApproval;
    }

    /// <summary>Human approval gate — sets Status = Approved before export is allowed.</summary>
    [HttpPost("approve")]
    [ProducesResponseType(typeof(GeneratedDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocument>> Approve([FromBody] ApproveGenerationRequest request, CancellationToken ct)
        => Ok(await _workflow.ApproveAsync(request, ct));

    [HttpPost("approvals/start")]
    [ProducesResponseType(typeof(MultiApprovalStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<MultiApprovalStatus>> StartMultiApproval(
        [FromBody] StartMultiApprovalRequest request,
        CancellationToken ct)
        => Ok(await _multiApproval.StartAsync(request, ct));

    [HttpPost("approvals/decide")]
    [ProducesResponseType(typeof(MultiApprovalStatus), StatusCodes.Status200OK)]
    public async Task<ActionResult<MultiApprovalStatus>> DecideApproval(
        [FromBody] ReviewerDecisionRequest request,
        CancellationToken ct)
    {
        try
        {
            return Ok(await _multiApproval.DecideAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }

    [HttpGet("approvals/{generationId:guid}")]
    [ProducesResponseType(typeof(MultiApprovalStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MultiApprovalStatus>> ApprovalStatus(Guid generationId, CancellationToken ct)
    {
        var status = await _multiApproval.GetStatusAsync(generationId, ct);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Quiet accept/edit/discard logging for future voice tuning.</summary>
    [HttpPost("feedback")]
    public async Task<IActionResult> Feedback([FromBody] GenerationFeedbackRequest request, CancellationToken ct)
    {
        await _workflow.LogFeedbackAsync(request, ct);
        return NoContent();
    }

    /// <summary>Export an approved draft as docx, pptx, or zip (SAS URL or local download path).</summary>
    [HttpPost("export")]
    [ProducesResponseType(typeof(ExportResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExportResult>> Export([FromBody] ExportGenerationRequest request, CancellationToken ct)
    {
        try
        {
            var multi = await _multiApproval.GetStatusAsync(request.GenerationId, ct);
            if (multi is not null && request.RequireApproved && !multi.IsFullyApproved)
            {
                return BadRequest(new
                {
                    title = $"Multi-reviewer approval incomplete (status: {multi.OverallStatus}). Legal and Sales must both approve."
                });
            }

            if (multi?.IsFullyApproved == true)
            {
                request.Document.Status = "Approved";
            }

            return Ok(await _workflow.ExportAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                title = "Export failed",
                detail = ex.Message
            });
        }
    }

    /// <summary>Download a locally cached export when Azurite is not running.</summary>
    [HttpGet("download/{folder}/{fileName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string folder, string fileName, [FromServices] IBlobStorageService blobs, CancellationToken ct)
    {
        var token = $"{folder}/{Uri.UnescapeDataString(fileName)}";
        var opened = await blobs.TryOpenLocalExportAsync(token, ct);
        if (opened is null)
        {
            return NotFound();
        }

        var (content, contentType, name) = opened.Value;
        return File(content, contentType, name);
    }
}
