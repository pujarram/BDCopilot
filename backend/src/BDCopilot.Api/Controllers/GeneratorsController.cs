using System.Text.Json;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BDCopilot.Api.Controllers;

/// <summary>RFP, business case and proposal generators. Every response is a Draft — see
/// GeneratedDocument.Status — there is no publish/send action here, by design.</summary>
[ApiController]
[Route("api/generate")]
[Produces("application/json")]
public class GeneratorsController : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDocumentGeneratorService _generator;
    private readonly CompliancePackSettings _compliance;

    public GeneratorsController(
        IDocumentGeneratorService generator,
        IOptions<CompliancePackSettings> compliance)
    {
        _generator = generator;
        _compliance = compliance.Value;
    }

    [HttpGet("compliance-packs")]
    [ProducesResponseType(typeof(CompliancePackSettings), StatusCodes.Status200OK)]
    public ActionResult<CompliancePackSettings> CompliancePacks()
        => Ok(_compliance);

    /// <summary>Draft an RFP response by reusing your strongest previous answers.</summary>
    [HttpPost("rfp")]
    [ProducesResponseType(typeof(GeneratedDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocument>> Rfp([FromBody] RfpGenerationRequest request, CancellationToken ct)
        => Ok(await _generator.GenerateRfpAsync(request, ct));

    /// <summary>
    /// Live RFP draft as Server-Sent Events (outline → tokens per section → complete document).
    /// </summary>
    [HttpPost("rfp/stream")]
    [Produces("text/event-stream")]
    public async Task RfpStream([FromBody] RfpGenerationRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in _generator.GenerateRfpStreamAsync(request, ct))
        {
            var json = JsonSerializer.Serialize(evt, StreamJson);
            await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>Draft a two-page executive business case for an initiative.</summary>
    [HttpPost("business-case")]
    [ProducesResponseType(typeof(GeneratedDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocument>> BusinessCase([FromBody] BusinessCaseGenerationRequest request, CancellationToken ct)
        => Ok(await _generator.GenerateBusinessCaseAsync(request, ct));

    /// <summary>Live business-case draft as Server-Sent Events.</summary>
    [HttpPost("business-case/stream")]
    [Produces("text/event-stream")]
    public async Task BusinessCaseStream([FromBody] BusinessCaseGenerationRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in _generator.GenerateBusinessCaseStreamAsync(request, ct))
        {
            var json = JsonSerializer.Serialize(evt, StreamJson);
            await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    /// <summary>Draft a full proposal narrative for a solution.</summary>
    [HttpPost("proposal")]
    [ProducesResponseType(typeof(GeneratedDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocument>> Proposal([FromBody] ProposalGenerationRequest request, CancellationToken ct)
        => Ok(await _generator.GenerateProposalAsync(request, ct));

    /// <summary>Live proposal draft as Server-Sent Events.</summary>
    [HttpPost("proposal/stream")]
    [Produces("text/event-stream")]
    public async Task ProposalStream([FromBody] ProposalGenerationRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in _generator.GenerateProposalStreamAsync(request, ct))
        {
            var json = JsonSerializer.Serialize(evt, StreamJson);
            await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }

    [HttpPost("competitive")]
    [ProducesResponseType(typeof(GeneratedDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<GeneratedDocument>> Competitive(
        [FromBody] CompetitivePositioningRequest request,
        CancellationToken ct)
        => Ok(await _generator.GenerateCompetitivePositioningAsync(request, ct));

    [HttpPost("competitive/stream")]
    [Produces("text/event-stream")]
    public async Task CompetitiveStream([FromBody] CompetitivePositioningRequest request, CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var evt in _generator.GenerateCompetitivePositioningStreamAsync(request, ct))
        {
            var json = JsonSerializer.Serialize(evt, StreamJson);
            await Response.WriteAsync($"event: {evt.Type}\ndata: {json}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
