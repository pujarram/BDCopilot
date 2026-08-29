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

    public WorkflowController(IGenerationWorkflowService workflow)
    {
        _workflow = workflow;
    }

    /// <summary>Human approval gate — sets Status = Approved before export is allowed.</summary>
    [HttpPost("approve")]
    [ProducesResponseType(typeof(GeneratedDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocument>> Approve([FromBody] ApproveGenerationRequest request, CancellationToken ct)
        => Ok(await _workflow.ApproveAsync(request, ct));

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
