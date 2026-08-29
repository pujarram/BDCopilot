using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

/// <summary>
/// History for Business Case and Proposal generators (stream → save → export → channel).
/// Routes: /api/history/business-case and /api/history/proposal
/// </summary>
[ApiController]
[Route("api/history/{documentType}")]
[Produces("application/json")]
public class DocumentHistoryController : ControllerBase
{
    private readonly IDocumentHistoryService _history;

    public DocumentHistoryController(IDocumentHistoryService history)
    {
        _history = history;
    }

    [HttpPost("save")]
    [ProducesResponseType(typeof(GeneratedDocumentHistory), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocumentHistory>> Save(
        string documentType,
        [FromBody] SaveGeneratedDocumentHistoryRequest request,
        CancellationToken ct)
    {
        // Route wins — body may omit documentType (Angular posts only document + user).
        request.DocumentType = documentType;
        return Ok(await _history.SaveAsync(request, ct));
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<GeneratedDocumentHistoryListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GeneratedDocumentHistoryListItem>>> List(
        string documentType,
        [FromQuery] string? userObjectId,
        CancellationToken ct)
        => Ok(await _history.ListAsync(documentType, userObjectId, ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GeneratedDocumentHistory), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GeneratedDocumentHistory>> Get(string documentType, Guid id, CancellationToken ct)
    {
        var row = await _history.GetAsync(id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost("{id:guid}/export")]
    [ProducesResponseType(typeof(ExportResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ExportResult>> Export(
        string documentType,
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
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { title = "Export failed", detail = ex.Message });
        }
    }

    [HttpPost("{id:guid}/save-to-channel")]
    [ProducesResponseType(typeof(GeneratedDocumentHistory), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocumentHistory>> SaveToChannel(
        string documentType,
        Guid id,
        [FromBody] SaveGeneratedDocumentToChannelRequest request,
        CancellationToken ct)
    {
        request.HistoryId = id;
        return Ok(await _history.SaveToBdChannelAsync(request, ct));
    }
}
