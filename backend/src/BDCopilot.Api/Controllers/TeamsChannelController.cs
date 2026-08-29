using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Teams;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BDCopilot.Api.Controllers;

/// <summary>
/// Microsoft Teams channel gateway — Bot Framework messaging endpoint for BD Copilot.
/// Azure Bot messaging endpoint: POST https://&lt;api-host&gt;/api/channels/teams/messages
/// </summary>
[ApiController]
[Route("api/channels/teams")]
[Produces("application/json")]
public class TeamsChannelController : ControllerBase
{
    private readonly ITeamsChannelService _service;
    private readonly TeamsBotAuthValidator _auth;
    private readonly ITeamsBotConnector _connector;
    private readonly ILogger<TeamsChannelController> _logger;

    public TeamsChannelController(
        ITeamsChannelService service,
        TeamsBotAuthValidator auth,
        ITeamsBotConnector connector,
        ILogger<TeamsChannelController> logger)
    {
        _service = service;
        _auth = auth;
        _connector = connector;
        _logger = logger;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public ActionResult<object> Health() => Ok(new
    {
        enabled = _auth.IsEnabled(),
        appIdConfigured = !string.IsNullOrWhiteSpace(_auth.ResolveAppId()),
        lastDeliveryStatus = _connector.LastDeliveryStatus,
        recentConnectorErrors = _connector.GetRecentErrors()
    });

    /// <summary>Browser / Azure probe — Bot Framework traffic is POST only.</summary>
    [HttpGet("messages")]
    [AllowAnonymous]
    public IActionResult MessagesProbe() => Ok(new
    {
        status = "ok",
        message = "BD Copilot Teams bot messaging endpoint. Azure Bot Service must POST Bot Framework activities here.",
        health = "/api/channels/teams/health"
    });

    /// <summary>Bot Framework messaging endpoint (Azure Bot → this URL).</summary>
    [HttpPost("messages")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveMessage([FromBody] TeamsActivity activity, CancellationToken _)
    {
        if (!await _auth.ValidateAsync(
                Request.Headers.Authorization.FirstOrDefault(),
                Request.Headers["X-Teams-Webhook-Secret"].FirstOrDefault(),
                Request.Headers["X-Webhook-Secret"].FirstOrDefault(),
                CancellationToken.None))
        {
            return Unauthorized(new { message = "Invalid Teams bot authentication." });
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var ct = cts.Token;

        try
        {
            _logger.LogInformation(
                "Teams activity type={Type} conversation={ConversationId} text={Text}",
                activity.Type,
                activity.Conversation?.Id,
                string.IsNullOrWhiteSpace(activity.Text) ? "(none)" : activity.Text.Trim());

            if (string.Equals(activity.Type, "message", StringComparison.OrdinalIgnoreCase)
                && _connector.CanSendViaConnector(activity))
            {
                await _connector.SendTypingAsync(activity, ct);
            }

            var replies = await _service.ProcessActivityAsync(activity, ct);
            return await DeliverAsync(activity, replies, ct);
        }
        catch (OperationCanceledException)
        {
            return await DeliverAsync(activity,
            [
                new TeamsReply
                {
                    Text = "That took too long. Confirm Ollama is running (llama3.1:8b) and try a shorter question."
                }
            ], ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teams channel processing failed.");
            return await DeliverAsync(activity,
            [
                new TeamsReply { Text = "Sorry — something went wrong. Check API logs and try again." }
            ], ct);
        }
    }

    private async Task<IActionResult> DeliverAsync(
        TeamsActivity inbound,
        IReadOnlyList<TeamsReply> replies,
        CancellationToken ct)
    {
        if (replies.Count == 0)
        {
            return Ok();
        }

        if (_connector.CanSendViaConnector(inbound))
        {
            var sent = await _connector.SendRepliesAsync(inbound, replies, ct);
            Response.Headers["X-BDCopilot-Teams-Delivery"] = sent ? "connector-ok" : "connector-failed";
            if (!sent)
            {
                _logger.LogError(
                    "Bot Connector failed to deliver replies. Status={Status}. Errors={Errors}",
                    _connector.LastDeliveryStatus,
                    string.Join(" | ", _connector.GetRecentErrors(3)));
            }

            return Ok();
        }

        Response.Headers["X-BDCopilot-Teams-Delivery"] = "http-body-fallback";
        _logger.LogWarning(
            "Bot Connector not used (missing serviceUrl/credentials). Returning body — Teams will not show this.");
        return Ok(replies.Count == 1 ? (object)replies[0] : replies);
    }
}
