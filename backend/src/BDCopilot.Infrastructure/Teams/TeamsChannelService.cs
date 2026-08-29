using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Teams;

/// <summary>
/// Routes Teams messages to BD Copilot chat / search. Full RFP UI stays in the tab app.
/// </summary>
public sealed class TeamsChannelService : ITeamsChannelService
{
    private readonly IAiChatService _chat;
    private readonly IVectorSearchService _search;
    private readonly TeamsBotSettings _settings;
    private readonly ILogger<TeamsChannelService> _logger;

    public TeamsChannelService(
        IAiChatService chat,
        IVectorSearchService search,
        IOptions<TeamsBotSettings> settings,
        ILogger<TeamsChannelService> logger)
    {
        _chat = chat;
        _search = search;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TeamsReply>> ProcessActivityAsync(TeamsActivity activity, CancellationToken ct = default)
    {
        if (!string.Equals(activity.Type, "message", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(activity.Type, "conversationUpdate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(activity.Type, "installationUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return [new TeamsReply { Text = BuildWelcome() }];
            }

            return Array.Empty<TeamsReply>();
        }

        var text = (activity.Text ?? string.Empty).Trim();
        // Strip Teams @mention markup like <at>BD Copilot</at>
        text = System.Text.RegularExpressions.Regex.Replace(text, "<at>[^<]*</at>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(text)
            || text.Equals("help", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hi", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hello", StringComparison.OrdinalIgnoreCase))
        {
            return [new TeamsReply { Text = BuildHelp() }];
        }

        var userObjectId = activity.From?.AadObjectId
                           ?? activity.From?.Id
                           ?? _settings.FallbackUserObjectId;

        try
        {
            if (text.StartsWith("search ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("find ", StringComparison.OrdinalIgnoreCase))
            {
                var query = text.Contains(' ') ? text[(text.IndexOf(' ') + 1)..].Trim() : text;
                return [new TeamsReply { Text = await SearchAsync(query, userObjectId, ct) }];
            }

            if (text.StartsWith("rfp", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("export", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("library", StringComparison.OrdinalIgnoreCase))
            {
                return [new TeamsReply { Text = BuildTabRedirect(text) }];
            }

            return [new TeamsReply { Text = await ChatAsync(text, userObjectId, ct) }];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teams message processing failed.");
            return
            [
                new TeamsReply
                {
                    Text = "Sorry — something went wrong. Confirm BDCopilot.Api and Ollama are running, then try again or open the BD Copilot tab."
                }
            ];
        }
    }

    private async Task<string> ChatAsync(string message, string userObjectId, CancellationToken ct)
    {
        var response = await _chat.AskAsync(new ChatRequest
        {
            Message = message,
            UserObjectId = userObjectId,
            History = []
        }, ct);

        var sb = new StringBuilder();
        sb.AppendLine(response.Answer?.Trim() ?? "(no answer)");
        if (response.Citations is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("**Sources**");
            foreach (var c in response.Citations.Take(5))
            {
                var locator = string.IsNullOrWhiteSpace(c.Locator) ? "" : $" · {c.Locator}";
                sb.AppendLine($"• {c.FileName}{locator}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"_Model: {response.AiProvider}/{response.Model}_");
        return sb.ToString();
    }

    private async Task<string> SearchAsync(string query, string userObjectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Usage: `search <keywords>` — e.g. `search wealth management RFP`";
        }

        var hits = await _search.SearchAsync(query, userObjectId, topK: 6, ct: ct);
        if (hits.Count == 0)
        {
            return $"No indexed documents matched **{query}**. Run SharePoint sync from the Library tab, then try again.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"**Search results for** _{query}_");
        sb.AppendLine();
        var i = 1;
        foreach (var hit in hits)
        {
            var locator = string.IsNullOrWhiteSpace(hit.Source.Locator) ? "" : $" · {hit.Source.Locator}";
            sb.AppendLine($"{i}. **{hit.Source.FileName}**{locator} (score {hit.Score:0.###})");
            if (!string.IsNullOrWhiteSpace(hit.Excerpt))
            {
                sb.AppendLine($"   {Truncate(hit.Excerpt, 180)}");
            }

            i++;
        }

        sb.AppendLine();
        sb.AppendLine($"Open full UI: {TabUrl("/search")}");
        return sb.ToString();
    }

    private string BuildWelcome() =>
        "Hi — I'm **BD Copilot** for Teams. Ask a BD question, or type `help` for commands.";

    private string BuildHelp()
    {
        var web = TabUrl("/chat");
        return
            """
            **BD Copilot — Teams commands**

            • Ask any BD question — RAG chat with citations
            • `search <keywords>` — find indexed SharePoint files
            • `rfp` / `library` — opens the full tab app for generators & documents
            • `help` — this message

            **Tabs (full UI):** Chat · RFP · Search · Library
            """.Replace("**Tabs (full UI):** Chat · RFP · Search · Library",
                $"**Tabs (full UI):** {web}");
    }

    private string BuildTabRedirect(string text)
    {
        var path = text.StartsWith("library", StringComparison.OrdinalIgnoreCase) ? "/library"
            : text.StartsWith("search", StringComparison.OrdinalIgnoreCase) ? "/search"
            : "/rfp";
        return
            $"For full **{(path == "/rfp" ? "RFP generator / export" : path.Trim('/'))}**, open the BD Copilot tab:\n{TabUrl(path)}\n\n"
            + "In chat I can answer questions and run `search …`.";
    }

    private string TabUrl(string path)
    {
        var baseUrl = (_settings.WebBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("YOUR-NGROK", StringComparison.OrdinalIgnoreCase))
        {
            return $"http://localhost:4200{path} (set TeamsBot:WebBaseUrl to your ngrok URL for Teams)";
        }

        return $"{baseUrl}{path}";
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
