using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/analytics")]
[Produces("application/json")]
public class AnalyticsController : ControllerBase
{
    private readonly IRoiAnalyticsService _roi;

    public AnalyticsController(IRoiAnalyticsService roi) => _roi = roi;

    [HttpGet("roi")]
    [ProducesResponseType(typeof(RoiDashboardSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<RoiDashboardSummary>> Roi(CancellationToken ct)
        => Ok(await _roi.GetRoiAsync(ct));

    [HttpGet("usage")]
    [ProducesResponseType(typeof(CustomerUsageSummary), StatusCodes.Status200OK)]
    public async Task<ActionResult<CustomerUsageSummary>> Usage(CancellationToken ct)
        => Ok(await _roi.GetCustomerUsageAsync(ct));
}
