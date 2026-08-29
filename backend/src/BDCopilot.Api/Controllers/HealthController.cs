using BDCopilot.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly BdCopilotDbContext _db;

    public HealthController(BdCopilotDbContext db)
    {
        _db = db;
    }

    /// <summary>Liveness/readiness check — also confirms the Postgres connection is reachable.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var canConnect = await _db.Database.CanConnectAsync(ct);
        return canConnect
            ? Ok(new { status = "healthy", database = "connected" })
            : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy", database = "unreachable" });
    }
}
