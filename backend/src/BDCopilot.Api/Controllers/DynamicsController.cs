using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/dynamics")]
[Produces("application/json")]
public class DynamicsController : ControllerBase
{
    private readonly IDynamicsDealService _dynamics;

    public DynamicsController(IDynamicsDealService dynamics) => _dynamics = dynamics;

    [HttpGet("deals")]
    [ProducesResponseType(typeof(List<DynamicsDealContext>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DynamicsDealContext>>> Deals(CancellationToken ct)
        => Ok(await _dynamics.ListDemoDealsAsync(ct));

    [HttpGet("deal-context")]
    [ProducesResponseType(typeof(DynamicsDealContext), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DynamicsDealContext>> DealContext(
        [FromQuery] string? opportunityId,
        [FromQuery] string? client,
        CancellationToken ct)
    {
        var ctx = await _dynamics.GetDealContextAsync(opportunityId, client, ct);
        return ctx is null ? NotFound() : Ok(ctx);
    }
}
