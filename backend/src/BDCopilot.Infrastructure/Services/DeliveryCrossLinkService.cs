using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public sealed class DeliveryCrossLinkService : IDeliveryCrossLinkService
{
    private static readonly string[] RfpNameTokens = ["rfp", "proposal", "bid", "pursuit", "commercial", "pricing"];
    private static readonly string[] ClauseTokens =
        ["scope", "security", "compliance", "architecture", "timeline", "delivery", "sla", "pricing", "requirement"];

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly BdCopilotDbContext _db;
    private readonly IPlannerSyncService _planner;
    private readonly IVectorSearchService _search;
    private readonly ILogger<DeliveryCrossLinkService> _logger;

    public DeliveryCrossLinkService(
        BdCopilotDbContext db,
        IPlannerSyncService planner,
        IVectorSearchService search,
        ILogger<DeliveryCrossLinkService> logger)
    {
        _db = db;
        _planner = planner;
        _search = search;
        _logger = logger;
    }

    public async Task<DeliveryCrossLinkResponse> GetCrossLinksAsync(
        string userObjectId,
        int maxTasks = 8,
        bool refresh = false,
        CancellationToken ct = default)
    {
        if (!refresh)
        {
            var freshCount = await _db.DeliveryCrossLinks.AsNoTracking()
                .CountAsync(l => l.ComputedAt >= DateTimeOffset.UtcNow - CacheTtl, ct);
            if (freshCount > 0)
            {
                return await LoadPersistedLinksAsync(maxTasks, ct);
            }
        }

        var delayed = await _planner.ListTasksAsync(delayedOnly: true, ct: ct);
        var links = new List<DeliveryCrossLink>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in delayed.Take(maxTasks))
        {
            var query = BuildCrossLinkQuery(task);
            if (string.IsNullOrWhiteSpace(query)) continue;

            List<SearchResultItem> hits;
            try
            {
                hits = await _search.SearchAsync(query, userObjectId, topK: 4, CorpusSources.Online, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SharePoint search failed for task {Title}", task.Title);
                continue;
            }

            foreach (var hit in hits)
            {
                var key = task.Id + "|" + hit.Source.DocumentId + "|" + (hit.Source.Locator ?? "");
                if (!seen.Add(key)) continue;

                var kind = ClassifyLinkKind(hit.Source.FileName, hit.Excerpt);
                var adjusted = await AdjustScoreAsync(hit.Source.DocumentId, task.Id, hit.Score, ct);
                links.Add(new DeliveryCrossLink
                {
                    TaskId = task.Id,
                    TaskTitle = task.Title,
                    Owner = task.AssignedUsers ?? "Unassigned",
                    IsDelayed = task.IsDelayed,
                    DocumentFileName = hit.Source.FileName,
                    Locator = hit.Source.Locator,
                    Excerpt = hit.Excerpt,
                    Score = adjusted,
                    LinkKind = kind,
                    Rationale = BuildRationale(task, hit, kind),
                    ComputedAt = DateTimeOffset.UtcNow
                });

                var persisted = await UpsertLinkAsync(task.Id, hit, kind, adjusted, ct);
                links[^1].LinkId = persisted.Id;
                links[^1].Upvotes = persisted.Upvotes;
                links[^1].Downvotes = persisted.Downvotes;
            }
        }

        await _db.SaveChangesAsync(ct);

        var rfpHits = links.Count(l => l.LinkKind == "RfpClause");
        return new DeliveryCrossLinkResponse
        {
            Links = links.OrderByDescending(l => l.Score).Take(20).ToList(),
            DelayedTaskCount = delayed.Count,
            RfpClauseHitCount = rfpHits,
            Summary = delayed.Count == 0
                ? "No delayed tasks — cross-links will appear when work slips past due dates."
                : $"Linked {links.Count} document hit(s) to {Math.Min(maxTasks, delayed.Count)} delayed task(s) " +
                  $"({rfpHits} RFP / winning-clause match(es))."
        };
    }

    public async Task<CrossLinkFeedbackResult> SubmitFeedbackAsync(
        CrossLinkFeedbackRequest request,
        CancellationToken ct = default)
    {
        var link = await _db.DeliveryCrossLinks.FirstOrDefaultAsync(l => l.Id == request.LinkId, ct)
                   ?? throw new InvalidOperationException("Cross-link not found.");

        switch (request.Action.Trim().ToLowerInvariant())
        {
            case "upvote":
            case "pin":
                link.Upvotes++;
                break;
            case "downvote":
            case "dismiss":
                link.Downvotes++;
                break;
            default:
                throw new InvalidOperationException($"Unknown feedback action: {request.Action}");
        }

        await _db.SaveChangesAsync(ct);
        var adjusted = ComputeAdjustedScore(link);
        return new CrossLinkFeedbackResult
        {
            LinkId = link.Id,
            Upvotes = link.Upvotes,
            Downvotes = link.Downvotes,
            AdjustedScore = adjusted
        };
    }

    private async Task<DeliveryCrossLinkResponse> LoadPersistedLinksAsync(int maxTasks, CancellationToken ct)
    {
        var records = await _db.DeliveryCrossLinks.AsNoTracking()
            .OrderByDescending(l => l.Score + l.Upvotes * 0.05 - l.Downvotes * 0.08)
            .Take(20)
            .ToListAsync(ct);

        if (records.Count == 0)
        {
            return new DeliveryCrossLinkResponse();
        }

        var taskIds = records.Select(r => r.TaskId).Distinct().ToList();
        var docIds = records.Select(r => r.DocumentId).Distinct().ToList();
        var tasks = await _db.PlannerTasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
        var docs = await _db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.DocumentId))
            .ToDictionaryAsync(d => d.DocumentId, ct);

        var links = records.Select(r =>
        {
            tasks.TryGetValue(r.TaskId, out var task);
            docs.TryGetValue(r.DocumentId, out var doc);
            return new DeliveryCrossLink
            {
                LinkId = r.Id,
                TaskId = r.TaskId,
                TaskTitle = task?.Title ?? "Task",
                Owner = task?.AssignedUsers ?? "Unassigned",
                IsDelayed = task?.IsDelayed ?? false,
                DocumentFileName = doc?.FileName ?? "Document",
                Locator = r.Locator,
                Excerpt = r.Excerpt,
                Score = ComputeAdjustedScore(r),
                LinkKind = r.LinkKind,
                Rationale = r.Rationale ?? "",
                ComputedAt = r.ComputedAt,
                Upvotes = r.Upvotes,
                Downvotes = r.Downvotes
            };
        }).ToList();

        return new DeliveryCrossLinkResponse
        {
            Links = links,
            DelayedTaskCount = await _db.PlannerTasks.CountAsync(t => t.IsDelayed, ct),
            RfpClauseHitCount = links.Count(l => l.LinkKind == "RfpClause"),
            Summary = $"Loaded {links.Count} persisted cross-link(s) from Postgres."
        };
    }

    private async Task<DeliveryCrossLinkRecord> UpsertLinkAsync(
        Guid taskId,
        SearchResultItem hit,
        string kind,
        double score,
        CancellationToken ct)
    {
        var existing = await _db.DeliveryCrossLinks
            .FirstOrDefaultAsync(l => l.TaskId == taskId && l.DocumentId == hit.Source.DocumentId, ct);
        if (existing is null)
        {
            existing = new DeliveryCrossLinkRecord
            {
                TaskId = taskId,
                DocumentId = hit.Source.DocumentId,
                LinkKind = kind,
                Score = score,
                Locator = hit.Source.Locator,
                Excerpt = hit.Excerpt,
                Rationale = $"Vector match score {score:0.###}",
                ComputedAt = DateTimeOffset.UtcNow
            };
            _db.DeliveryCrossLinks.Add(existing);
        }
        else
        {
            existing.Score = score;
            existing.LinkKind = kind;
            existing.Locator = hit.Source.Locator;
            existing.Excerpt = hit.Excerpt;
            existing.ComputedAt = DateTimeOffset.UtcNow;
        }

        return existing;
    }

    private async Task<double> AdjustScoreAsync(Guid documentId, Guid taskId, double baseScore, CancellationToken ct)
    {
        var row = await _db.DeliveryCrossLinks.AsNoTracking()
            .FirstOrDefaultAsync(l => l.TaskId == taskId && l.DocumentId == documentId, ct);
        return row is null ? baseScore : ComputeAdjustedScore(row);
    }

    private static double ComputeAdjustedScore(DeliveryCrossLinkRecord row) =>
        Math.Round(row.Score + row.Upvotes * 0.05 - row.Downvotes * 0.08, 4);

    private static string BuildCrossLinkQuery(PlannerTaskListItem task)
    {
        var title = task.Title.Trim();
        var bucket = task.BucketName?.Trim() ?? "";
        var tokens = title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 3)
            .Take(5);
        var q = string.Join(' ', tokens);
        if (!string.IsNullOrWhiteSpace(bucket)) q += " " + bucket;
        return string.IsNullOrWhiteSpace(q) ? title : q;
    }

    private static string ClassifyLinkKind(string fileName, string? excerpt)
    {
        var text = (fileName + " " + (excerpt ?? "")).ToLowerInvariant();
        var isRfpDoc = RfpNameTokens.Any(t => text.Contains(t, StringComparison.Ordinal));
        var hasClause = ClauseTokens.Any(t => text.Contains(t, StringComparison.Ordinal));
        return isRfpDoc && hasClause ? "RfpClause" : "DeliveryDoc";
    }

    private static string BuildRationale(PlannerTaskListItem task, SearchResultItem hit, string kind) =>
        kind == "RfpClause"
            ? $"Delayed “{task.Title}” ↔ winning RFP language in {hit.Source.FileName} ({task.AssignedUsers ?? "Unassigned"})"
            : $"Delayed “{task.Title}” ↔ related delivery doc {hit.Source.FileName} ({task.AssignedUsers ?? "Unassigned"})";
}
