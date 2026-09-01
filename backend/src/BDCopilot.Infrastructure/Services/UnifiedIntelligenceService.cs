using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public sealed class UnifiedIntelligenceService : IUnifiedIntelligenceService
{
    private readonly IPlannerSyncService _planner;
    private readonly IProjectManagerService _projectManager;
    private readonly IVectorSearchService _search;
    private readonly IAiChatService _chat;
    private readonly ILogger<UnifiedIntelligenceService> _logger;

    public UnifiedIntelligenceService(
        IPlannerSyncService planner,
        IProjectManagerService projectManager,
        IVectorSearchService search,
        IAiChatService chat,
        ILogger<UnifiedIntelligenceService> logger)
    {
        _planner = planner;
        _projectManager = projectManager;
        _search = search;
        _chat = chat;
        _logger = logger;
    }

    public async Task<UnifiedIntelligenceResponse> QueryAsync(
        UnifiedIntelligenceRequest request,
        CancellationToken ct = default)
    {
        var insight = await _projectManager.GetInsightsAsync(ct);
        var delayed = request.IncludeDelayedTasks
            ? await _planner.ListTasksAsync(delayedOnly: true, ct: ct)
            : [];

        var related = new List<UnifiedRelatedDocument>();
        if (request.IncludeSharePoint && delayed.Count > 0)
        {
            related = await FindRelatedDocumentsAsync(delayed, request.UserObjectId, ct);
        }

        var summary = BuildDeterministicSummary(request.Query, insight, delayed, related);
        var usedLlm = false;
        string? provider = null;
        string? model = null;

        if (request.UseLlmSummary)
        {
            try
            {
                var context = BuildLlmContext(insight, delayed, related);
                var question = string.IsNullOrWhiteSpace(request.Query)
                    ? "Write a unified management brief: delayed work, related documents, owners, and recommended actions."
                    : request.Query!;

                var reply = await _chat.AskAsync(new ChatRequest
                {
                    Message = question,
                    UserObjectId = request.UserObjectId,
                    History = [],
                    ExtraContext = context
                }, ct);

                if (!string.IsNullOrWhiteSpace(reply.Answer))
                {
                    summary = reply.Answer.Trim();
                    usedLlm = true;
                    provider = reply.AiProvider;
                    model = reply.Model;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unified intelligence LLM summary failed; using deterministic brief.");
            }
        }

        return new UnifiedIntelligenceResponse
        {
            Query = request.Query,
            HealthScore = insight.HealthScore,
            RiskLevel = insight.RiskLevel,
            ManagementSummary = summary,
            DelayedTasks = delayed.Take(20).ToList(),
            RelatedDocuments = related,
            StaffingRecommendations = insight.StaffingRecommendations,
            UsedLlmSummary = usedLlm,
            AiProvider = provider,
            Model = model,
            GeneratedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task<List<UnifiedRelatedDocument>> FindRelatedDocumentsAsync(
        IReadOnlyList<PlannerTaskListItem> delayed,
        string userObjectId,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<UnifiedRelatedDocument>();

        foreach (var task in delayed.Take(6))
        {
            var query = BuildSearchQuery(task);
            if (string.IsNullOrWhiteSpace(query)) continue;

            List<SearchResultItem> hits;
            try
            {
                hits = await _search.SearchAsync(query, userObjectId, topK: 3, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SharePoint search failed for task {Title}", task.Title);
                continue;
            }

            foreach (var hit in hits)
            {
                var key = hit.Source.FileName + "|" + (hit.Source.Locator ?? "");
                if (!seen.Add(key)) continue;

                results.Add(new UnifiedRelatedDocument
                {
                    FileName = hit.Source.FileName,
                    Locator = hit.Source.Locator,
                    Excerpt = hit.Excerpt,
                    Score = hit.Score,
                    MatchedTaskTitle = task.Title
                });
            }
        }

        return results.OrderByDescending(r => r.Score).Take(12).ToList();
    }

    private static string BuildSearchQuery(PlannerTaskListItem task)
    {
        var title = task.Title.Trim();
        if (title.Length < 4) return title;
        // Drop generic planner words; keep module-ish tokens
        var tokens = title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 3 && !w.Equals("task", StringComparison.OrdinalIgnoreCase))
            .Take(4);
        var q = string.Join(' ', tokens);
        return string.IsNullOrWhiteSpace(q) ? title : q;
    }

    private static string BuildDeterministicSummary(
        string? query,
        ProjectManagerInsight insight,
        IReadOnlyList<PlannerTaskListItem> delayed,
        IReadOnlyList<UnifiedRelatedDocument> related)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(query))
        {
            sb.AppendLine($"**Query:** {query}");
            sb.AppendLine();
        }

        sb.AppendLine(
            $"Health **{insight.HealthScore}/100** ({insight.RiskLevel}). " +
            $"{delayed.Count} delayed task(s), {related.Count} related document hit(s).");
        sb.AppendLine();

        if (delayed.Count > 0)
        {
            sb.AppendLine("**Delayed tasks & owners**");
            foreach (var t in delayed.Take(8))
            {
                sb.AppendLine($"• {t.Title} — {t.AssignedUsers ?? "Unassigned"} (due {t.DueDate?.UtcDateTime:yyyy-MM-dd})");
            }

            sb.AppendLine();
        }

        if (related.Count > 0)
        {
            sb.AppendLine("**Related SharePoint / library docs**");
            foreach (var d in related.Take(6))
            {
                sb.AppendLine($"• {d.FileName} (for: {d.MatchedTaskTitle})");
            }

            sb.AppendLine();
        }

        if (insight.StaffingRecommendations.Count > 0)
        {
            sb.AppendLine("**Recommended actions**");
            foreach (var s in insight.StaffingRecommendations.Take(4))
            {
                sb.AppendLine($"• [{s.Priority}] {s.Focus}: {s.Detail}");
            }
        }

        return sb.ToString().Trim();
    }

    private static string BuildLlmContext(
        ProjectManagerInsight insight,
        IReadOnlyList<PlannerTaskListItem> delayed,
        IReadOnlyList<UnifiedRelatedDocument> related)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Health={insight.HealthScore}, Risk={insight.RiskLevel}");
        sb.AppendLine("Delayed tasks:");
        foreach (var t in delayed.Take(12))
        {
            sb.AppendLine($"- {t.Title} | owner={t.AssignedUsers} | due={t.DueDate:yyyy-MM-dd} | {t.PercentComplete}%");
        }

        sb.AppendLine("Related docs:");
        foreach (var d in related.Take(10))
        {
            sb.AppendLine($"- {d.FileName} | task={d.MatchedTaskTitle} | score={d.Score:0.###}");
            if (!string.IsNullOrWhiteSpace(d.Excerpt))
            {
                sb.AppendLine($"  {d.Excerpt[..Math.Min(d.Excerpt.Length, 160)]}");
            }
        }

        sb.AppendLine("Staffing:");
        foreach (var s in insight.StaffingRecommendations.Take(6))
        {
            sb.AppendLine($"- [{s.Priority}] {s.Focus}: {s.Detail}");
        }

        return sb.ToString();
    }
}
