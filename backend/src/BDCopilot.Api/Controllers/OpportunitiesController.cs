using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/opportunities")]
[Produces("application/json")]
public class OpportunitiesController : ControllerBase
{
    private readonly IOpportunityService _opportunities;

    public OpportunitiesController(IOpportunityService opportunities) => _opportunities = opportunities;

    [HttpGet]
    [ProducesResponseType(typeof(List<Opportunity>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<Opportunity>>> List(CancellationToken ct)
        => Ok(await _opportunities.ListAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Opportunity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Opportunity>> Get(Guid id, CancellationToken ct)
    {
        var row = await _opportunities.GetAsync(id, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost]
    [ProducesResponseType(typeof(Opportunity), StatusCodes.Status200OK)]
    public async Task<ActionResult<Opportunity>> Create([FromBody] CreateOpportunityRequest request, CancellationToken ct)
        => Ok(await _opportunities.CreateAsync(request, ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(Opportunity), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Opportunity>> Update(Guid id, [FromBody] UpdateOpportunityRequest request, CancellationToken ct)
    {
        var row = await _opportunities.UpdateAsync(id, request, ct);
        return row is null ? NotFound() : Ok(row);
    }

    [HttpPost("link-document")]
    [ProducesResponseType(typeof(OpportunityDocumentLink), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpportunityDocumentLink>> Link(
        [FromBody] LinkOpportunityDocumentRequest request,
        CancellationToken ct)
        => Ok(await _opportunities.LinkDocumentAsync(request, ct));

    [HttpPost("win-loss")]
    public async Task<IActionResult> TagWinLoss([FromBody] TagWinLossRequest request, CancellationToken ct)
    {
        try
        {
            await _opportunities.TagWinLossAsync(request, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { title = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }
}
