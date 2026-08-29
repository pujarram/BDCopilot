using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Teams;

/// <summary>
/// Posts reply activities to the Bot Framework Connector (Teams ignores the webhook HTTP body).
/// </summary>
public sealed class TeamsBotConnector : ITeamsBotConnector
{
    private static readonly ConcurrentQueue<string> RecentErrors = new();
    private static string? _lastDeliveryStatus;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly TeamsBotSettings _settings;
    private readonly GraphSyncSettings _graph;
    private readonly ILogger<TeamsBotConnector> _logger;

    public TeamsBotConnector(
        HttpClient http,
        IOptions<TeamsBotSettings> settings,
        IOptions<GraphSyncSettings> graph,
        ILogger<TeamsBotConnector> logger)
    {
        _http = http;
        _settings = settings.Value;
        _graph = graph.Value;
        _logger = logger;
    }

    public string? LastDeliveryStatus => _lastDeliveryStatus;

    public IReadOnlyList<string> GetRecentErrors(int take = 5) =>
        RecentErrors.Reverse().Take(Math.Max(1, take)).ToList();

    public bool CanSendViaConnector(TeamsActivity inbound) =>
        !string.IsNullOrWhiteSpace(inbound.ServiceUrl)
        && !string.IsNullOrWhiteSpace(inbound.Conversation?.Id)
        && inbound.Recipient?.Id is not null
        && !string.IsNullOrWhiteSpace(ResolveAppId())
        && !string.IsNullOrWhiteSpace(ResolveAppPassword());

    public async Task SendTypingAsync(TeamsActivity inbound, CancellationToken ct = default)
    {
        if (!CanSendViaConnector(inbound))
        {
            return;
        }

        try
        {
            await PostActivityAsync(inbound, new
            {
                type = "typing",
                from = new { id = inbound.Recipient!.Id, name = inbound.Recipient.Name },
                recipient = new { id = inbound.From?.Id, name = inbound.From?.Name },
                conversation = new { id = inbound.Conversation!.Id },
                replyToId = inbound.Id
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Typing indicator failed (ignored).");
        }
    }

    public async Task<bool> SendRepliesAsync(
        TeamsActivity inbound,
        IReadOnlyList<TeamsReply> replies,
        CancellationToken ct = default)
    {
        if (replies.Count == 0)
        {
            _lastDeliveryStatus = "skipped-empty";
            return true;
        }

        if (!CanSendViaConnector(inbound))
        {
            RememberError("Missing serviceUrl, conversation, recipient, or bot credentials.");
            _lastDeliveryStatus = "skipped-incomplete-inbound";
            return false;
        }

        try
        {
            foreach (var reply in replies)
            {
                await PostActivityAsync(inbound, new
                {
                    type = "message",
                    text = reply.Text,
                    from = new { id = inbound.Recipient!.Id, name = inbound.Recipient.Name },
                    recipient = new { id = inbound.From?.Id, name = inbound.From?.Name },
                    conversation = new { id = inbound.Conversation!.Id },
                    replyToId = inbound.Id
                }, ct);
            }

            _lastDeliveryStatus = "ok";
            return true;
        }
        catch (Exception ex)
        {
            RememberError(Truncate(ex.Message, 400));
            _lastDeliveryStatus = "failed";
            _logger.LogError(ex, "Bot Connector reply failed for conversation {ConversationId}.", inbound.Conversation?.Id);
            return false;
        }
    }

    private async Task PostActivityAsync(TeamsActivity inbound, object activity, CancellationToken ct)
    {
        var token = await AcquireBotTokenAsync(ct);
        var baseUrl = inbound.ServiceUrl!.TrimEnd('/');
        var conversationId = Uri.EscapeDataString(inbound.Conversation!.Id!);
        string url;
        if (!string.IsNullOrWhiteSpace(inbound.Id))
        {
            var activityId = Uri.EscapeDataString(inbound.Id);
            url = $"{baseUrl}/v3/conversations/{conversationId}/activities/{activityId}";
        }
        else
        {
            url = $"{baseUrl}/v3/conversations/{conversationId}/activities";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(activity, JsonOpts), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Bot Connector HTTP {(int)response.StatusCode}: {Truncate(body, 500)}");
        }
    }

    private async Task<string> AcquireBotTokenAsync(CancellationToken ct)
    {
        var appId = ResolveAppId();
        var password = ResolveAppPassword();
        var tenant = FirstNonEmpty(_settings.TenantId, _graph.TenantId, "botframework.com");

        var tokenUrl = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = appId,
            ["client_secret"] = password,
            ["scope"] = "https://api.botframework.com/.default"
        });

        using var response = await _http.PostAsync(tokenUrl, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Bot token request failed HTTP {(int)response.StatusCode}: {Truncate(body, 400)}. "
                + "Confirm TeamsBot/Graph ClientId + ClientSecret and that Azure Bot uses the same app id.");
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("access_token", out var tokenEl))
        {
            throw new InvalidOperationException("Bot token response missing access_token.");
        }

        return tokenEl.GetString()
               ?? throw new InvalidOperationException("Bot token response access_token was null.");
    }

    private string ResolveAppId() => FirstNonEmpty(_settings.MicrosoftAppId, _graph.ClientId);

    private string ResolveAppPassword() => FirstNonEmpty(_settings.MicrosoftAppPassword, _graph.ClientSecret);

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    private static void RememberError(string message)
    {
        RecentErrors.Enqueue($"{DateTimeOffset.UtcNow:HH:mm:ss} {message}");
        while (RecentErrors.Count > 20 && RecentErrors.TryDequeue(out _))
        {
        }
    }

    private static string Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text.Length <= max ? text : text[..max] + "…";
}
