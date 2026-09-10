using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Teams;

/// <summary>
/// Routes Teams messages to BD Copilot chat / search / Project Intelligence.
/// Full RFP UI and dashboards stay in the tab app.
/// </summary>
public sealed class TeamsChannelService : ITeamsChannelService
{
    private static readonly Regex AssigneeCommand = new(
        @"^(?:tasks?\s+for|assignee|assigned\s+to)\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IAiChatService _chat;
    private readonly IVectorSearchService _search;
    private readonly IPlannerSyncService _planner;
    private readonly IProjectManagerService _projectManager;
    private readonly IUnifiedIntelligenceService _unified;
    private readonly IPlannerSnapshotService _snapshots;
    private readonly IPipelineCapacityService _pipeline;
    private readonly IDocumentGeneratorService _generator;
    private readonly TeamsBotSettings _settings;
    private readonly ILogger<TeamsChannelService> _logger;

    public TeamsChannelService(
        IAiChatService chat,
        IVectorSearchService search,
        IPlannerSyncService planner,
        IProjectManagerService projectManager,
        IUnifiedIntelligenceService unified,
        IPlannerSnapshotService snapshots,
        IPipelineCapacityService pipeline,
        IDocumentGeneratorService generator,
        IOptions<TeamsBotSettings> settings,
        ILogger<TeamsChannelService> logger)
    {
        _chat = chat;
        _search = search;
        _planner = planner;
        _projectManager = projectManager;
        _unified = unified;
        _snapshots = snapshots;
        _pipeline = pipeline;
        _generator = generator;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TeamsReply>> ProcessActivityAsync(TeamsActivity activity, CancellationToken ct = default)
    {
        var userObjectId = activity.From?.AadObjectId
                           ?? activity.From?.Id
                           ?? _settings.FallbackUserObjectId;

        // Adaptive Card Action.Submit / messageBack may arrive as invoke.
        if (string.Equals(activity.Type, "invoke", StringComparison.OrdinalIgnoreCase))
        {
            var invokeText = ExtractInvokeText(activity);
            if (!string.IsNullOrWhiteSpace(invokeText))
            {
                activity = new TeamsActivity
                {
                    Type = "message",
                    Text = invokeText,
                    From = activity.From,
                    Recipient = activity.Recipient,
                    Conversation = activity.Conversation,
                    ServiceUrl = activity.ServiceUrl,
                    ChannelId = activity.ChannelId
                };
            }
            else
            {
                return Array.Empty<TeamsReply>();
            }
        }

        if (!string.Equals(activity.Type, "message", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(activity.Type, "conversationUpdate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(activity.Type, "installationUpdate", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    new TeamsReply
                    {
                        Text = BuildWelcome(),
                        AdaptiveCard = TeamsAdaptiveCardBuilder.QuickPromptChipsCard(QuickAskChips())
                    }
                ];
            }

            return Array.Empty<TeamsReply>();
        }

        var text = (activity.Text ?? string.Empty).Trim();
        text = Regex.Replace(text, "<at>[^<]*</at>", "", RegexOptions.IgnoreCase).Trim();

        if (string.IsNullOrWhiteSpace(text)
            || text.Equals("hi", StringComparison.OrdinalIgnoreCase)
            || text.Equals("hello", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new TeamsReply
                {
                    Text = BuildWelcome(),
                    AdaptiveCard = TeamsAdaptiveCardBuilder.QuickPromptChipsCard(QuickAskChips())
                }
            ];
        }

        if (text.Equals("help", StringComparison.OrdinalIgnoreCase)
            || text.Equals("chips", StringComparison.OrdinalIgnoreCase)
            || text.Equals("suggest", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new TeamsReply
                {
                    Text = BuildHelp(),
                    AdaptiveCard = TeamsAdaptiveCardBuilder.QuickPromptChipsCard(QuickAskChips())
                }
            ];
        }

        try
        {
            if (text.Equals("cancel generate", StringComparison.OrdinalIgnoreCase)
                || text.Equals("cancel", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("cancel generate", StringComparison.OrdinalIgnoreCase))
            {
                return [new TeamsReply { Text = "Cancelled — no document was generated." }];
            }

            var confirmReply = await TryConfirmGenerateAsync(text, userObjectId, ct);
            if (confirmReply is not null)
            {
                return [confirmReply];
            }

            var generateOffer = TryOfferGenerateConfirm(text);
            if (generateOffer is not null)
            {
                return [generateOffer];
            }

            if (text.StartsWith("search ", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("find ", StringComparison.OrdinalIgnoreCase))
            {
                var query = text.Contains(' ') ? text[(text.IndexOf(' ') + 1)..].Trim() : text;
                return [await SearchAsync(query, userObjectId, ct)];
            }

            if (text.StartsWith("library", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("export", StringComparison.OrdinalIgnoreCase)
                || text.Equals("rfp", StringComparison.OrdinalIgnoreCase)
                || text.Equals("business case", StringComparison.OrdinalIgnoreCase)
                || text.Equals("proposal", StringComparison.OrdinalIgnoreCase)
                || text.Equals("battlecard", StringComparison.OrdinalIgnoreCase)
                || text.Equals("battle-card", StringComparison.OrdinalIgnoreCase)
                || text.Equals("battle card", StringComparison.OrdinalIgnoreCase)
                || text.Equals("competitive", StringComparison.OrdinalIgnoreCase))
            {
                return [new TeamsReply { Text = BuildTabRedirect(text) }];
            }

            var battlecardReply = await TryBattleCardCommandAsync(text, userObjectId, ct);
            if (battlecardReply is not null)
            {
                return [battlecardReply];
            }

            var plannerReply = await TryPlannerCommandAsync(text, userObjectId, ct);
            if (plannerReply is not null)
            {
                return [plannerReply];
            }

            return [await ChatAsync(text, userObjectId, extraContext: null, ct)];
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

    private async Task<TeamsReply?> TryPlannerCommandAsync(string text, string userObjectId, CancellationToken ct)
    {
        var lower = text.ToLowerInvariant();

        if (IsExactOrStarts(lower, "projects", "planner", "dashboard"))
        {
            return Reply(
                $"**Project Intelligence** dashboard:\n{TabUrl("/projects")}\n\n"
                + "Try: `delayed`, `workload`, `sprint`, `due next week`, or `tasks for <name>`.");
        }

        if (IsExactOrContains(lower, "stalled", "no progress", "stuck")
            && LooksLikeProjectIntent(lower))
        {
            var alerts = await _snapshots.GetStalledAlertsAsync(7, ct);
            var dash = TabUrl("/projects?view=stalled");
            var textBody = alerts.Count == 0
                ? "**No stalled tasks** — progress changed within 7 days."
                : "**Stalled tasks (7+ days)**\n" + string.Join("\n",
                    alerts.Take(6).Select(a => $"• {a.Title} — {a.PercentComplete}% · {a.AssignedUsers ?? "Unassigned"}"));
            return Reply(
                textBody + $"\n\n{dash}",
                TeamsAdaptiveCardBuilder.StalledAlertCard(alerts, dash));
        }

        if (IsExactOrContains(lower, "pursuit", "deadline", "closing soon")
            && (LooksLikeProjectIntent(lower) || lower.Contains("pursuit") || lower.Contains("deadline")))
        {
            var alerts = await _pipeline.GetPursuitDeadlineAlertsAsync(14, ct);
            var dash = TabUrl("/projects?view=pipeline");
            var textBody = alerts.Count == 0
                ? "**No pursuit deadlines** in the next 14 days."
                : "**Pursuit deadlines (14 days)**\n" + string.Join("\n",
                    alerts.Take(6).Select(a => $"• {a.Name} ({a.Client}) — {a.DaysRemaining}d · {a.Stage}"));
            return Reply(
                textBody + $"\n\n{dash}",
                TeamsAdaptiveCardBuilder.PursuitDeadlineCard(alerts, dash));
        }

        if (IsExactOrContains(lower, "stale", "outdated document", "old document")
            && (LooksLikeDocQuestion(lower) || lower.Contains("stale")))
        {
            var alerts = await _pipeline.GetStaleDocumentAlertsAsync(ct);
            var dash = TabUrl("/library?stale=true");
            var textBody = alerts.Count == 0
                ? "**No stale library documents** detected."
                : "**Stale documents**\n" + string.Join("\n",
                    alerts.Take(6).Select(a => $"• {a.FileName} — {a.MonthsSinceModified}mo old"));
            return Reply(
                textBody + $"\n\n{dash}",
                TeamsAdaptiveCardBuilder.StaleDocumentCard(alerts, dash));
        }

        if (IsExactOrContains(lower, "delayed", "overdue")
            && !LooksLikeDocQuestion(lower))
        {
            var tasks = await _planner.ListTasksAsync(delayedOnly: true, ct: ct);
            var dash = TabUrl("/projects?view=overview&delayedOnly=true");
            var textBody = FormatTaskList("**Delayed tasks**", tasks, dash);
            var top = tasks.Count == 0
                ? "No delayed tasks."
                : string.Join("; ", tasks.Take(4).Select(t => t.Title));
            return Reply(
                textBody,
                TeamsAdaptiveCardBuilder.DelayedTasksCard(tasks.Count, top, dash));
        }

        if (IsExactOrContains(lower, "workload", "team load", "capacity"))
        {
            var rows = await _planner.GetWorkloadAsync(ct);
            return Reply(FormatWorkload(rows, TabUrl("/projects?view=workload")));
        }

        if (lower is "report" or "weekly report" or "stakeholder report"
            || (IsExactOrContains(lower, "weekly report", "stakeholder report") && LooksLikeProjectIntent(lower)))
        {
            return Reply(await FormatStakeholderReportAsync(userObjectId, ct));
        }

        if (lower is "unified" or "brief" or "management brief"
            || lower.StartsWith("unified ", StringComparison.Ordinal)
            || (lower.Contains("unified") && LooksLikeProjectIntent(lower)))
        {
            var query = lower.StartsWith("unified ", StringComparison.Ordinal)
                ? text["unified ".Length..].Trim()
                : null;
            return await FormatUnifiedAsync(query, userObjectId, ct);
        }

        if (lower is "insights" or "predict" or "health score"
            || IsExactOrContains(lower, "insights", "predict", "at risk", "health score")
            || (lower.Contains("risk") && LooksLikeProjectIntent(lower) && !LooksLikeDocQuestion(lower)))
        {
            return await FormatInsightsAsync(ct);
        }

        if (IsExactOrContains(lower, "sprint", "health", "summary", "status")
            && LooksLikeProjectIntent(lower))
        {
            return await FormatSprintSummaryAsync(ct);
        }

        if (lower.Contains("due next week")
            || lower.Contains("due this week")
            || lower.Equals("due week")
            || lower.StartsWith("due "))
        {
            var (from, to, label) = ResolveDueWindow(lower);
            var tasks = await _planner.ListTasksAsync(dueFrom: from, dueTo: to, ct: ct);
            var qs = $"view=overview&dueFrom={Uri.EscapeDataString(from.ToString("o"))}&dueTo={Uri.EscapeDataString(to.ToString("o"))}";
            return Reply(FormatTaskList($"**Tasks {label}**", tasks, TabUrl($"/projects?{qs}")));
        }

        var assigneeMatch = AssigneeCommand.Match(text);
        if (assigneeMatch.Success)
        {
            var name = assigneeMatch.Groups[1].Value.Trim();
            var tasks = await _planner.ListTasksAsync(assigneeContains: name, ct: ct);
            return Reply(FormatTaskList(
                $"**Tasks for** _{name}_",
                tasks,
                TabUrl($"/projects?view=overview&assignee={Uri.EscapeDataString(name)}")));
        }

        // Free-form project NL: ground on Planner snapshot + optional SharePoint hits.
        if (LooksLikeProjectIntent(lower))
        {
            var snapshot = await BuildPlannerSnapshotAsync(ct);
            var reply = await ChatAsync(text, userObjectId, snapshot, ct);
            reply.Text += $"\n\nOpen dashboard: {TabUrl("/projects")}";
            return reply;
        }

        return null;
    }

    private static TeamsReply Reply(string text, object? adaptiveCard = null) =>
        new() { Text = text, AdaptiveCard = adaptiveCard };

    private async Task<TeamsReply> FormatUnifiedAsync(string? query, string userObjectId, CancellationToken ct)
    {
        var result = await _unified.QueryAsync(new UnifiedIntelligenceRequest
        {
            Query = query,
            UserObjectId = userObjectId,
            IncludeDelayedTasks = true,
            IncludeSharePoint = true,
            UseLlmSummary = true
        }, ct);

        var sb = new StringBuilder();
        sb.AppendLine("**Unified intelligence brief**");
        sb.AppendLine();
        sb.AppendLine($"Health **{result.HealthScore}/100** · Risk **{result.RiskLevel}**");
        sb.AppendLine();
        sb.AppendLine(result.ManagementSummary);

        if (result.RelatedDocuments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Related documents**");
            foreach (var d in result.RelatedDocuments.Take(5))
            {
                sb.AppendLine($"• {d.FileName} _(for {d.MatchedTaskTitle})_");
            }
        }

        if (result.UsedLlmSummary)
        {
            sb.AppendLine();
            sb.AppendLine($"_Summary: {result.AiProvider}/{result.Model}_");
        }

        sb.AppendLine();
        sb.AppendLine($"Dashboard: {TabUrl("/projects?view=unified")}");
        var dash = TabUrl("/projects?view=unified");
        return Reply(
            sb.ToString(),
            TeamsAdaptiveCardBuilder.UnifiedBriefCard(
                result.HealthScore,
                result.RiskLevel,
                result.ManagementSummary,
                result.DelayedTasks.Count,
                result.RelatedDocuments.Count,
                dash));
    }

    private async Task<TeamsReply> FormatInsightsAsync(CancellationToken ct)
    {
        var insight = await _projectManager.GetInsightsAsync(ct);
        var sb = new StringBuilder();
        sb.AppendLine("**AI Project Manager — insights**");
        sb.AppendLine();
        sb.AppendLine($"• Health score: **{insight.HealthScore}/100** · Risk: **{insight.RiskLevel}**");
        sb.AppendLine($"• {insight.Summary.Replace("**", "")}");
        sb.AppendLine();
        sb.AppendLine("**Delay predictions**");
        foreach (var p in insight.DelayPredictions.Take(6))
        {
            sb.AppendLine($"• [{p.RiskLevel}] {p.Title} — slip ≈{p.PredictedSlipDays}d · {p.Rationale}");
        }

        if (insight.ModulesAtRisk.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Modules at risk**");
            foreach (var m in insight.ModulesAtRisk.Take(5))
            {
                sb.AppendLine($"• [{m.RiskLevel}] {m.ModuleName} — {m.DelayedCount} delayed · {m.Recommendation}");
            }
        }

        if (insight.StaffingRecommendations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Staffing**");
            foreach (var s in insight.StaffingRecommendations.Take(5))
            {
                sb.AppendLine($"• [{s.Priority}] {s.Focus}: {s.Detail}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Dashboard: {TabUrl("/projects?view=insights")}");
        sb.AppendLine($"Weekly report: type `report` · DOCX: {TabUrl("/projects?view=insights")}");
        var dash = TabUrl("/projects?view=insights");
        var health = await _planner.GetHealthSummaryAsync(ct);
        return Reply(
            sb.ToString(),
            TeamsAdaptiveCardBuilder.HealthSummaryCard(
                insight.HealthScore,
                insight.RiskLevel,
                health.TotalTasks,
                health.Delayed,
                health.CompletionPercent,
                dash));
    }

    private async Task<string> FormatStakeholderReportAsync(string userObjectId, CancellationToken ct)
    {
        var report = await _projectManager.GenerateStakeholderReportAsync(useLlmNarrative: true, userObjectId, ct);
        var sb = new StringBuilder();
        sb.AppendLine($"**{report.Title}**");
        sb.AppendLine();
        // Teams-friendly: strip markdown headings noise lightly
        foreach (var line in report.MarkdownBody.Split('\n').Take(40))
        {
            var t = line.Trim();
            if (string.IsNullOrWhiteSpace(t)) continue;
            sb.AppendLine(t.TrimStart('#').Trim());
        }

        if (report.UsedLlmNarrative)
        {
            sb.AppendLine();
            sb.AppendLine($"_Narrative: {report.AiProvider}/{report.Model}_");
        }

        sb.AppendLine();
        sb.AppendLine($"Dashboard: {TabUrl("/projects?view=insights")}");
        return sb.ToString();
    }

    private async Task<TeamsReply> FormatSprintSummaryAsync(CancellationToken ct)
    {
        var health = await _planner.GetHealthSummaryAsync(ct);
        var insight = await _projectManager.GetInsightsAsync(ct);
        var delayed = await _planner.ListTasksAsync(delayedOnly: true, ct: ct);
        var sb = new StringBuilder();
        sb.AppendLine("**Sprint / plan summary**");
        sb.AppendLine();
        sb.AppendLine($"• Plans: **{health.PlanCount}** · Tasks: **{health.TotalTasks}** · Completion: **{health.CompletionPercent:0.#}%**");
        sb.AppendLine($"• Done: {health.Completed} · In progress: {health.InProgress} · Not started: {health.NotStarted}");
        sb.AppendLine($"• Delayed: **{health.Delayed}** · Risk: **{health.RiskLevel}**");
        if (health.LastSyncAt.HasValue)
        {
            sb.AppendLine($"• Last sync: {health.LastSyncAt.Value.UtcDateTime:yyyy-MM-dd HH:mm} UTC");
        }

        if (delayed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Top delayed**");
            foreach (var t in delayed.Take(5))
            {
                sb.AppendLine($"• {FormatTaskLine(t)}");
            }
        }

        var dash = TabUrl("/projects?view=overview");
        sb.AppendLine();
        sb.AppendLine($"Dashboard: {dash}");
        return Reply(
            sb.ToString(),
            TeamsAdaptiveCardBuilder.HealthSummaryCard(
                insight.HealthScore,
                health.RiskLevel,
                health.TotalTasks,
                health.Delayed,
                health.CompletionPercent,
                dash));
    }

    private async Task<string> BuildPlannerSnapshotAsync(CancellationToken ct)
    {
        var health = await _planner.GetHealthSummaryAsync(ct);
        var delayed = await _planner.ListTasksAsync(delayedOnly: true, ct: ct);
        var upcoming = await _planner.ListTasksAsync(
            dueFrom: DateTimeOffset.UtcNow.Date,
            dueTo: DateTimeOffset.UtcNow.Date.AddDays(7),
            ct: ct);
        var workload = await _planner.GetWorkloadAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine(
            $"Health: plans={health.PlanCount}, tasks={health.TotalTasks}, completed={health.Completed}, " +
            $"inProgress={health.InProgress}, delayed={health.Delayed}, completion={health.CompletionPercent:0.#}%, risk={health.RiskLevel}");
        sb.AppendLine("Delayed tasks:");
        foreach (var t in delayed.Take(12))
        {
            sb.AppendLine($"- {t.Title} | due={FmtDate(t.DueDate)} | {t.PercentComplete}% | {t.AssignedUsers ?? "Unassigned"} | {t.BucketName}");
        }

        sb.AppendLine("Due in next 7 days:");
        foreach (var t in upcoming.Take(12))
        {
            sb.AppendLine($"- {t.Title} | due={FmtDate(t.DueDate)} | {t.PercentComplete}% | {t.AssignedUsers ?? "Unassigned"}");
        }

        sb.AppendLine("Workload:");
        foreach (var w in workload.Take(10))
        {
            sb.AppendLine($"- {w.Assignee}: total={w.TotalTasks}, delayed={w.Delayed}, avg%={w.AvgPercentComplete:0}");
        }

        return sb.ToString();
    }

    private async Task<TeamsReply> ChatAsync(string message, string userObjectId, string? extraContext, CancellationToken ct)
    {
        var response = await _chat.AskAsync(new ChatRequest
        {
            Message = message,
            UserObjectId = userObjectId,
            History = [],
            ExtraContext = extraContext,
            IncludeChannelLiveSearch = true
        }, ct);

        var sb = new StringBuilder();
        sb.AppendLine(response.Answer?.Trim() ?? "I couldn't find relevant indexed content to answer that question.");
        if (response.Citations is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("**Sources**");
            var apiBase = ApiBaseUrl();
            foreach (var c in response.Citations.Take(6))
            {
                sb.AppendLine(CitationOpenUrl.FormatMarkdownLink(c, apiBase, userObjectId));
                if (!string.IsNullOrWhiteSpace(c.Snippet))
                {
                    sb.AppendLine($"  _{Truncate(c.Snippet, 160)}_");
                }
            }

            if (response.Citations.Count > 6)
            {
                sb.AppendLine($"_…and {response.Citations.Count - 6} more in the BD Copilot tab._");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"_Model: {response.AiProvider}/{response.Model}_");

        var card = TeamsAdaptiveCardBuilder.GroundedSourcesCard(
            response.Answer ?? "",
            response.Citations ?? [],
            ApiBaseUrl(),
            userObjectId,
            TabUrl("/chat"));

        return Reply(sb.ToString(), card);
    }

    private async Task<TeamsReply> SearchAsync(string query, string userObjectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Reply("Usage: `search <keywords>` — e.g. `search wealth management RFP`");
        }

        var hits = await _search.SearchAsync(query, userObjectId, topK: 6, ct: ct);
        if (hits.Count == 0)
        {
            return Reply(
                $"No indexed documents matched **{query}**. Run SharePoint sync from the Library tab, then try again.");
        }

        var apiBase = ApiBaseUrl();
        var sb = new StringBuilder();
        sb.AppendLine($"**Search results for** _{query}_");
        sb.AppendLine();
        var i = 1;
        foreach (var hit in hits)
        {
            var label = CitationOpenUrl.FormatLabel(hit.Source);
            var url = CitationOpenUrl.Resolve(hit.Source, apiBase, userObjectId);
            sb.AppendLine(url is null
                ? $"{i}. **{label}** (score {hit.Score:0.###})"
                : $"{i}. [{label}]({url}) (score {hit.Score:0.###})");
            if (!string.IsNullOrWhiteSpace(hit.Excerpt))
            {
                sb.AppendLine($"   {Truncate(hit.Excerpt, 180)}");
            }

            i++;
        }

        sb.AppendLine();
        sb.AppendLine($"Open full UI: {TabUrl("/search")}");

        var card = TeamsAdaptiveCardBuilder.GroundedSourcesCard(
            $"Top matches for \"{query}\"",
            hits.Select(h => h.Source).ToList(),
            apiBase,
            userObjectId,
            TabUrl("/search"));

        return Reply(sb.ToString(), card);
    }

    private string BuildWelcome() =>
        "Hi — I'm **BD Copilot** for Teams. Tap a quick ask below, type `generate rfp …` for Confirm/Cancel drafting, or `help` for commands.";

    private string BuildHelp()
    {
        var projects = TabUrl("/projects");
        var chat = TabUrl("/chat");
        return
            $"""
            **BD Copilot — Teams commands**

            **Quick asks**
            • Tap a chip below, or type `chips`

            **Generate (Confirm / Cancel)**
            • `generate rfp <topic> for <customer>`
            • `generate business case <initiative>`
            • `generate proposal <solution>`
            • `generate battlecard vs <competitor>`

            **Project Intelligence**
            • `delayed` — overdue Planner tasks
            • `tasks for <name>` — filter by assignee
            • `due next week` / `due this week` — upcoming due dates
            • `workload` — team assignment load
            • `sprint` / `health` — plan summary + risk
            • `insights` / `predict` — health score, delay risk, staffing
            • `report` — stakeholder weekly status
            • `unified` — delayed tasks + SharePoint docs + management brief
            • `projects` — open dashboard ({projects})

            **Documents**
            • Ask any BD question — RAG chat with citations
            • `search <keywords>` — find indexed SharePoint files
            • `battlecard vs <competitor>` — generate card + objection Adaptive Card
            • `rfp` / `library` / `battlecard` — open generator tabs

            **Tabs:** {chat}
            """;
    }

    private static List<(string Label, string Prompt)> QuickAskChips() =>
    [
        ("GDPR for banking", "How should GDPR and data residency be described to banking clients?"),
        ("Wealth security", "What security and compliance topics are covered for wealth proposals?"),
        ("Wealth proposal structure", "Summarize the wealth proposal template structure and recommended sections."),
        ("Retail ROI", "What ROI and payback guidance appears in the retail business case?"),
        ("Generate RFP", "generate rfp Wealth Management Platform for ABC Bank"),
        ("Generate Battle Card", "generate battlecard vs Incumbent Chatbot"),
        ("Delayed tasks", "delayed"),
        ("Sprint health", "sprint")
    ];

    private static string? ExtractInvokeText(TeamsActivity activity)
    {
        if (activity.Value is null || activity.Value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        var value = activity.Value.Value;
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
            {
                return textProp.GetString();
            }

            if (value.TryGetProperty("msteams", out var teams)
                && teams.ValueKind == JsonValueKind.Object
                && teams.TryGetProperty("text", out var mbText)
                && mbText.ValueKind == JsonValueKind.String)
            {
                return mbText.GetString();
            }

            if (value.TryGetProperty("action", out var action)
                && action.ValueKind == JsonValueKind.Object
                && action.TryGetProperty("data", out var data))
            {
                if (data.ValueKind == JsonValueKind.String) return data.GetString();
                if (data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("text", out var dataText)
                    && dataText.ValueKind == JsonValueKind.String)
                {
                    return dataText.GetString();
                }
            }
        }

        return null;
    }

    private TeamsReply? TryOfferGenerateConfirm(string text)
    {
        var m = Regex.Match(
            text,
            @"^generate\s+(rfp|business[\s-]?case|proposal|battle[\s-]?card)\b(?:\s+vs\.?)?\s*(.+)?$",
            RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            // Also accept "create rfp …"
            m = Regex.Match(
                text,
                @"^create\s+(rfp|business[\s-]?case|proposal|battle[\s-]?card)\b(?:\s+vs\.?)?\s*(.+)?$",
                RegexOptions.IgnoreCase);
        }

        if (!m.Success) return null;

        var kindRaw = m.Groups[1].Value.Trim().ToLowerInvariant();
        var topic = (m.Groups[2].Success ? m.Groups[2].Value : "").Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            topic = kindRaw.Contains("battle") ? "Incumbent Chatbot"
                : kindRaw.Contains("business") ? "AI Wealth Copilot Platform"
                : kindRaw.Contains("proposal") ? "BD Copilot grounded assistant"
                : "Wealth Management Platform for ABC Bank";
        }

        var (kind, label, path) = NormalizeGeneratorKind(kindRaw);
        var confirmCmd = $"confirm generate {kind}|{topic}";
        var card = TeamsAdaptiveCardBuilder.GeneratorConfirmCard(
            label,
            topic,
            confirmCmd,
            "cancel generate",
            TabUrl(path));

        return Reply(
            $"Ready to draft **{label}** for:\n_{topic}_\n\nTap **Confirm** to generate, or **Cancel**.",
            card);
    }

    private async Task<TeamsReply?> TryConfirmGenerateAsync(
        string text,
        string userObjectId,
        CancellationToken ct)
    {
        if (!text.StartsWith("confirm generate ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var payload = text["confirm generate ".Length..].Trim();
        var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;

        var kind = parts[0].Trim().ToLowerInvariant();
        var topic = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1]
            : "Wealth Management Platform";

        _logger.LogInformation("Teams confirm generate {Kind} topic={Topic}", kind, topic);

        try
        {
            return kind switch
            {
                "rfp" => await RunRfpGenerateAsync(topic, userObjectId, ct),
                "business-case" => await RunBusinessCaseGenerateAsync(topic, userObjectId, ct),
                "proposal" => await RunProposalGenerateAsync(topic, userObjectId, ct),
                "battlecard" => await RunBattleCardGenerateAsync(topic, userObjectId, ct),
                _ => new TeamsReply
                {
                    Text = "Unknown generator. Use `generate rfp …`, `generate business case …`, `generate proposal …`, or `generate battlecard vs …`."
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teams confirm generate failed for {Kind}", kind);
            return new TeamsReply
            {
                Text = $"Generation failed ({kind}). Open the full tab and try again, or confirm Ollama / Azure OpenAI is running."
            };
        }
    }

    private async Task<TeamsReply> RunRfpGenerateAsync(string topic, string userObjectId, CancellationToken ct)
    {
        var (title, customer) = SplitTopicAndCustomer(topic);
        var doc = await _generator.GenerateRfpAsync(
            new RfpGenerationRequest
            {
                Title = title,
                Customer = customer,
                UserObjectId = userObjectId,
                ComplianceRegion = "EU"
            },
            ct);
        return BuildGeneratorResultReply(doc, "/rfp");
    }

    private async Task<TeamsReply> RunBusinessCaseGenerateAsync(string topic, string userObjectId, CancellationToken ct)
    {
        var doc = await _generator.GenerateBusinessCaseAsync(
            new BusinessCaseGenerationRequest
            {
                Initiative = topic,
                UserObjectId = userObjectId,
                ComplianceRegion = "EU"
            },
            ct);
        return BuildGeneratorResultReply(doc, "/business-case");
    }

    private async Task<TeamsReply> RunProposalGenerateAsync(string topic, string userObjectId, CancellationToken ct)
    {
        var doc = await _generator.GenerateProposalAsync(
            new ProposalGenerationRequest
            {
                Solution = topic,
                UserObjectId = userObjectId,
                ComplianceRegion = "EU"
            },
            ct);
        return BuildGeneratorResultReply(doc, "/proposal");
    }

    private async Task<TeamsReply> RunBattleCardGenerateAsync(string topic, string userObjectId, CancellationToken ct)
    {
        var competitor = topic;
        if (competitor.StartsWith("vs ", StringComparison.OrdinalIgnoreCase))
        {
            competitor = competitor[3..].Trim();
        }

        var doc = await _generator.GenerateCompetitivePositioningAsync(
            new CompetitivePositioningRequest
            {
                Competitor = competitor,
                OurSolution = "BD Copilot grounded assistant",
                UserObjectId = userObjectId,
                SourceType = "corpus",
                ComplianceRegion = "EU"
            },
            ct);
        return BuildGeneratorResultReply(doc, "/battle-card");
    }

    private TeamsReply BuildGeneratorResultReply(GeneratedDocument doc, string path)
    {
        var preview = doc.Sections.Count == 0
            ? "No sections returned — check corpus / AI provider."
            : string.Join("\n\n", doc.Sections.Take(2).Select(s => $"**{s.Title}**\n{Truncate(s.Content, 280)}"));

        var sb = new StringBuilder();
        sb.AppendLine($"**{doc.Title}** — draft ready ({doc.Sections.Count} sections)");
        sb.AppendLine();
        sb.AppendLine(preview);
        sb.AppendLine();
        sb.AppendLine($"Edit / export in the tab: {TabUrl(path)}");

        return Reply(
            sb.ToString(),
            TeamsAdaptiveCardBuilder.GeneratorResultCard(
                doc.Title,
                doc.Sections.Count,
                preview.Replace("**", ""),
                TabUrl(path)));
    }

    private static (string Kind, string Label, string Path) NormalizeGeneratorKind(string kindRaw)
    {
        var k = kindRaw.Replace(" ", "-").ToLowerInvariant();
        if (k.StartsWith("business")) return ("business-case", "Business Case", "/business-case");
        if (k.StartsWith("proposal")) return ("proposal", "Proposal", "/proposal");
        if (k.StartsWith("battle")) return ("battlecard", "Battle Card", "/battle-card");
        return ("rfp", "RFP", "/rfp");
    }

    private static (string Title, string Customer) SplitTopicAndCustomer(string topic)
    {
        var m = Regex.Match(topic, @"^(.+?)\s+for\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
        }

        return (topic, "Customer");
    }

    private string BuildTabRedirect(string text)
    {
        var lower = text.Trim().ToLowerInvariant();
        var path = lower.StartsWith("library") ? "/library"
            : lower.StartsWith("search") ? "/search"
            : lower.StartsWith("battle") || lower.StartsWith("competitive") ? "/battle-card"
            : "/rfp";
        var label = path switch
        {
            "/library" => "document library",
            "/search" => "knowledge search",
            "/battle-card" => "Battle Card generator",
            _ => "RFP generator / export"
        };
        return
            $"For full **{label}**, open the BD Copilot tab:\n{TabUrl(path)}\n\n"
            + "In chat I can answer questions, run `search …`, `battlecard vs <competitor>`, and Planner commands like `delayed`.";
    }

    private async Task<TeamsReply?> TryBattleCardCommandAsync(
        string text,
        string userObjectId,
        CancellationToken ct)
    {
        var lower = text.Trim();
        if (!lower.StartsWith("battlecard", StringComparison.OrdinalIgnoreCase)
            && !lower.StartsWith("battle-card", StringComparison.OrdinalIgnoreCase)
            && !lower.StartsWith("battle card", StringComparison.OrdinalIgnoreCase)
            && !lower.StartsWith("objections vs", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Exact tab redirects already handled; require a competitor name.
        var competitor = ExtractBattleCardCompetitor(lower);
        if (string.IsNullOrWhiteSpace(competitor))
        {
            return Reply(
                "Usage: `battlecard vs Incumbent Chatbot` — or open the full generator:\n"
                + TabUrl("/battle-card"));
        }

        _logger.LogInformation("Teams battlecard generate vs {Competitor}", competitor);
        var doc = await _generator.GenerateCompetitivePositioningAsync(
            new CompetitivePositioningRequest
            {
                Competitor = competitor,
                OurSolution = "BD Copilot grounded assistant",
                UserObjectId = userObjectId,
                SourceType = "corpus",
                ComplianceRegion = "EU"
            },
            ct);

        var objections = ParseObjectionPairs(
            doc.Sections.FirstOrDefault(s =>
                s.Title.Contains("Objection", StringComparison.OrdinalIgnoreCase))?.Content ?? "");
        var winThemes = SplitBullets(
            doc.Sections.FirstOrDefault(s =>
                s.Title.Contains("Win", StringComparison.OrdinalIgnoreCase))?.Content ?? "");

        var sb = new StringBuilder();
        sb.AppendLine($"**{doc.Title}**");
        sb.AppendLine();
        foreach (var section in doc.Sections.Take(4))
        {
            sb.AppendLine($"**{section.Title}**");
            sb.AppendLine(Truncate(section.Content, 420));
            sb.AppendLine();
        }

        sb.AppendLine($"Full card: {TabUrl("/battle-card")}");

        var card = TeamsAdaptiveCardBuilder.BattleCardObjectionsCard(
            doc.Title,
            competitor,
            objections,
            winThemes,
            TabUrl("/battle-card"));

        return Reply(sb.ToString(), card);
    }

    private static string? ExtractBattleCardCompetitor(string text)
    {
        var m = Regex.Match(
            text,
            @"^(?:battle[\s-]?card|objections)\s+(?:vs\.?|against)\s+(.+)$",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.Trim();

        m = Regex.Match(text, @"^battle[\s-]?card\s+(.+)$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var rest = m.Groups[1].Value.Trim();
            if (rest.Equals("vs", StringComparison.OrdinalIgnoreCase)) return null;
            return rest;
        }

        return null;
    }

    private static List<(string Objection, string Response)> ParseObjectionPairs(string content)
    {
        var pairs = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(content)) return pairs;

        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? pending = null;
        foreach (var line in lines)
        {
            var cleaned = Regex.Replace(line, @"^[\-\*\u2022›>]+\s*", "");
            if (cleaned.StartsWith("Objection:", StringComparison.OrdinalIgnoreCase))
            {
                pending = cleaned["Objection:".Length..].Trim();
            }
            else if (cleaned.StartsWith("Response:", StringComparison.OrdinalIgnoreCase) && pending is not null)
            {
                pairs.Add((pending, cleaned["Response:".Length..].Trim()));
                pending = null;
            }
        }

        return pairs;
    }

    private static List<string> SplitBullets(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Regex.Replace(l, @"^[\-\*\u2022›>]+\s*", "").Trim())
            .Where(l => l.Length > 0)
            .Take(6)
            .ToList();
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

    private string ApiBaseUrl()
    {
        var url = (_settings.ApiBaseUrl ?? "").TrimEnd('/');
        if (string.IsNullOrWhiteSpace(url))
        {
            return "http://localhost:5154";
        }

        return url;
    }

    private static string FormatTaskList(string heading, IReadOnlyList<PlannerTaskListItem> tasks, string dashboardUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine(heading);
        sb.AppendLine();
        if (tasks.Count == 0)
        {
            sb.AppendLine("_No matching tasks._");
        }
        else
        {
            foreach (var t in tasks.Take(12))
            {
                sb.AppendLine($"• {FormatTaskLine(t)}");
            }

            if (tasks.Count > 12)
            {
                sb.AppendLine($"_…and {tasks.Count - 12} more_");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Dashboard: {dashboardUrl}");
        return sb.ToString();
    }

    private static string FormatWorkload(IReadOnlyList<PlannerWorkloadRow> rows, string dashboardUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**Team workload**");
        sb.AppendLine();
        if (rows.Count == 0)
        {
            sb.AppendLine("_No assignments yet. Run Planner sync from the dashboard._");
        }
        else
        {
            foreach (var w in rows.Take(12))
            {
                sb.AppendLine(
                    $"• **{w.Assignee}** — {w.TotalTasks} tasks · {w.InProgress} in progress · **{w.Delayed} delayed** · avg {w.AvgPercentComplete:0}%");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Dashboard: {dashboardUrl}");
        return sb.ToString();
    }

    private static string FormatTaskLine(PlannerTaskListItem t)
    {
        var due = FmtDate(t.DueDate);
        var who = string.IsNullOrWhiteSpace(t.AssignedUsers) ? "Unassigned" : t.AssignedUsers;
        var flag = t.IsDelayed ? " ⚠ delayed" : "";
        return $"{t.Title} — due {due} · {t.PercentComplete}% · {who}{flag}";
    }

    private static string FmtDate(DateTimeOffset? d) =>
        d.HasValue ? d.Value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—";

    private static (DateTimeOffset from, DateTimeOffset to, string label) ResolveDueWindow(string lower)
    {
        var today = DateTime.UtcNow.Date;
        if (lower.Contains("this week"))
        {
            var start = today.AddDays(-(int)today.DayOfWeek);
            var end = start.AddDays(7).AddTicks(-1);
            return (new DateTimeOffset(start, TimeSpan.Zero), new DateTimeOffset(end, TimeSpan.Zero), "due this week");
        }

        // default: next 7 days from today (also covers "due next week")
        var from = today;
        var to = today.AddDays(7).AddTicks(-1);
        if (lower.Contains("next week"))
        {
            var nextMonday = today.AddDays(7 - (int)today.DayOfWeek);
            if (today.DayOfWeek == DayOfWeek.Sunday)
            {
                nextMonday = today.AddDays(1);
            }

            from = nextMonday;
            to = nextMonday.AddDays(7).AddTicks(-1);
            return (new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(to, TimeSpan.Zero), "due next week");
        }

        return (new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(to, TimeSpan.Zero), "due in the next 7 days");
    }

    private static bool IsExactOrStarts(string lower, params string[] keys) =>
        keys.Any(k => lower.Equals(k, StringComparison.Ordinal) || lower.StartsWith(k + " ", StringComparison.Ordinal));

    private static bool IsExactOrContains(string lower, params string[] keys) =>
        keys.Any(k => lower.Equals(k, StringComparison.Ordinal) || lower.Contains(k, StringComparison.Ordinal));

  private static bool LooksLikeProjectIntent(string lower) =>
        lower.Contains("project")
        || lower.Contains("planner")
        || lower.Contains("sprint")
        || lower.Contains("gantt")
        || lower.Contains("timeline")
        || lower.Contains("workload")
        || lower.Contains("delayed")
        || lower.Contains("overdue")
        || lower.Contains("task")
        || lower.Contains("assignee")
        || lower.Contains("milestone")
        || lower.Contains("due ")
        || lower.Contains("insight")
        || lower.Contains("report")
        || lower.Contains("stakeholder")
        || lower.Contains("staffing")
        || lower.Contains("stalled")
        || lower.Contains("stuck");

    private static bool LooksLikeDocQuestion(string lower) =>
        lower.Contains("rfp")
        || lower.Contains("document")
        || lower.Contains("sharepoint")
        || lower.Contains("proposal")
        || lower.Contains("citation");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
