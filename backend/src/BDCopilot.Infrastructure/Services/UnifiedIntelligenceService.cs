using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public sealed class UnifiedIntelligenceService : IUnifiedIntelligenceService
{
    private static readonly string[] RfpNameTokens = ["rfp", "proposal", "bid", "pursuit", "commercial"];

    private readonly IPlannerSyncService _planner;
    private readonly IProjectManagerService _projectManager;
    private readonly IVectorSearchService _search;
    private readonly IDeliveryCrossLinkService _crossLinks;
    private readonly IAiChatService _chat;
    private readonly ILogger<UnifiedIntelligenceService> _logger;

    public UnifiedIntelligenceService(
        IPlannerSyncService planner,
        IProjectManagerService projectManager,
        IVectorSearchService search,
        IDeliveryCrossLinkService crossLinks,
        IAiChatService chat,
        ILogger<UnifiedIntelligenceService> logger)
    {
        _planner = planner;
        _projectManager = projectManager;
        _search = search;
        _crossLinks = crossLinks;
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
            var crossLinkResponse = await _crossLinks.GetCrossLinksAsync(request.UserObjectId, maxTasks: 8, refresh: false, ct);
            related = crossLinkResponse.Links.Select(l => new UnifiedRelatedDocument
            {
                FileName = l.DocumentFileName,
                Locator = l.Locator,
                Excerpt = l.Excerpt,
                Score = l.Score,
                MatchedTaskId = l.TaskId,
                MatchedTaskTitle = l.TaskTitle,
                OwnerDisplayName = l.Owner,
                LinkKind = l.LinkKind,
                Rationale = l.Rationale
            }).ToList();

            if (related.Count == 0)
            {
                related = await FindRelatedDocumentsAsync(delayed, request.UserObjectId, ct);
            }
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
                    ? "Write a unified management brief: delayed work, related RFP clauses, owners, and recommended actions."
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
                hits = await _search.SearchAsync(query, userObjectId, topK: 3, CorpusSources.Online, ct);
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

                var kind = ClassifyLinkKind(hit.Source.FileName, hit.Excerpt);
                results.Add(new UnifiedRelatedDocument
                {
                    FileName = hit.Source.FileName,
                    Locator = hit.Source.Locator,
                    Excerpt = hit.Excerpt,
                    Score = hit.Score,
                    MatchedTaskId = task.Id,
                    MatchedTaskTitle = task.Title,
                    OwnerDisplayName = task.AssignedUsers ?? "Unassigned",
                    LinkKind = kind,
                    Rationale = kind == "RfpClause"
                        ? $"Delayed task ↔ winning RFP clause ({task.AssignedUsers ?? "Unassigned"})"
                        : $"Delayed task ↔ delivery doc ({task.AssignedUsers ?? "Unassigned"})"
                });
            }
        }

        return results.OrderByDescending(r => r.Score).Take(12).ToList();
    }

    private static string ClassifyLinkKind(string fileName, string? excerpt)
    {
        var text = (fileName + " " + (excerpt ?? "")).ToLowerInvariant();
        return RfpNameTokens.Any(t => text.Contains(t, StringComparison.Ordinal)) ? "RfpClause" : "DeliveryDoc";
    }

    private static string BuildSearchQuery(PlannerTaskListItem task)
    {
        var title = task.Title.Trim();
        if (title.Length < 4) return title;
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

        var rfpLinks = related.Count(r => r.LinkKind == "RfpClause");
        sb.AppendLine(
            $"Health **{insight.HealthScore}/100** ({insight.RiskLevel}). " +
            $"{delayed.Count} delayed task(s), {related.Count} cross-link(s) ({rfpLinks} RFP clause match(es)).");
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
            sb.AppendLine("**Cross-links: delayed work ↔ RFP / delivery docs**");
            foreach (var d in related.Take(6))
            {
                sb.AppendLine($"• {d.MatchedTaskTitle} ↔ {d.FileName} [{d.LinkKind}] — {d.OwnerDisplayName}");
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

        sb.AppendLine("Cross-links (delayed ↔ RFP clauses ↔ owners):");
        foreach (var d in related.Take(10))
        {
            sb.AppendLine($"- task={d.MatchedTaskTitle} | owner={d.OwnerDisplayName} | doc={d.FileName} | kind={d.LinkKind} | score={d.Score:0.###}");
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
