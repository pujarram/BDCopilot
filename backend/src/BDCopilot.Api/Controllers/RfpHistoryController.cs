using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/rfp")]
[Produces("application/json")]
public class RfpHistoryController : ControllerBase
{
    private readonly IRfpHistoryService _history;

    public RfpHistoryController(IRfpHistoryService history)
    {
        _history = history;
    }

    /// <summary>Persist generated RFP sections into PostgreSQL history.</summary>
    [HttpPost("save")]
    [ProducesResponseType(typeof(RfpDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<RfpDocument>> Save([FromBody] SaveRfpDocumentRequest request, CancellationToken ct)
        => Ok(await _history.SaveAsync(request, ct));

    /// <summary>List saved RFP drafts (optionally filtered by creator).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<RfpDocumentListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<RfpDocumentListItem>>> List([FromQuery] string? userObjectId, CancellationToken ct)
        => Ok(await _history.ListAsync(userObjectId, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(RfpDocument), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RfpDocument>> Get(Guid id, CancellationToken ct)
    {
        var row = await _history.GetAsync(id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    /// <summary>Export a saved RFP as docx/pptx/zip.</summary>
    [HttpPost("{id:guid}/export")]
    [ProducesResponseType(typeof(ExportResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExportResult>> Export(
        Guid id,
        [FromQuery] string userObjectId,
        [FromQuery] string format = "docx",
        CancellationToken ct = default)
    {
        try
        {
            return Ok(await _history.ExportAsync(id, userObjectId, format, ct));
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { title = "Export failed", detail = ex.Message });
        }
    }

    /// <summary>Upload Word file to Teams BD channel RFP folder (Graph) when configured.</summary>
    [HttpPost("{id:guid}/save-to-channel")]
    [ProducesResponseType(typeof(RfpDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<RfpDocument>> SaveToChannel(
        Guid id,
        [FromBody] SaveRfpToChannelRequest request,
        CancellationToken ct)
    {
        request.RfpDocumentId = id;
        return Ok(await _history.SaveToBdChannelAsync(request, ct));
    }
}
