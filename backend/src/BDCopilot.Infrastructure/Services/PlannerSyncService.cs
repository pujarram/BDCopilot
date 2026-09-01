using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using GraphClientFactory = BDCopilot.Infrastructure.Graph.GraphClientFactory;
using GraphPlannerTask = Microsoft.Graph.Models.PlannerTask;
using PlanEntity = BDCopilot.Core.Models.PlannerPlan;
using BucketEntity = BDCopilot.Core.Models.PlannerBucket;
using TaskEntity = BDCopilot.Core.Models.PlannerTaskItem;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Syncs Microsoft Planner plans/buckets/tasks into Postgres for Project Intelligence.
/// Falls back to demo seed when Graph is missing or Planner permissions are not consented.
/// </summary>
public class PlannerSyncService : IPlannerSyncService
{
    private readonly BdCopilotDbContext _db;
    private readonly GraphClientFactory _graphFactory;
    private readonly PlannerSyncSettings _settings;
    private readonly IPlannerSnapshotService _snapshots;
    private readonly ILogger<PlannerSyncService> _logger;

    public PlannerSyncService(
        BdCopilotDbContext db,
        GraphClientFactory graphFactory,
        IOptions<PlannerSyncSettings> settings,
        IPlannerSnapshotService snapshots,
        ILogger<PlannerSyncService> logger)
    {
        _db = db;
        _graphFactory = graphFactory;
        _settings = settings.Value;
        _snapshots = snapshots;
        _logger = logger;
    }

    public async Task<PlannerSyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!_settings.Enabled)
        {
            return new PlannerSyncResult { StatusMessage = "Planner sync is disabled (Planner:Enabled=false)." };
        }

        var graph = _graphFactory.GetClient();
        if (graph is null)
        {
            if (_settings.SeedDemoData)
            {
                return await SeedDemoAsync(ct);
            }

            return new PlannerSyncResult
            {
                StatusMessage = "Graph is not configured and SeedDemoData is false."
            };
        }

        try
        {
            var planIds = await ResolvePlanIdsAsync(graph, ct);
            if (planIds.Count == 0)
            {
                if (_settings.SeedDemoData)
                {
                    _logger.LogInformation("No Planner plan ids configured/discovered — seeding demo data.");
                    return await SeedDemoAsync(ct);
                }

                return new PlannerSyncResult
                {
                    StatusMessage = "No Planner:PlanIds or Planner:GroupIds configured."
                };
            }

            var result = new PlannerSyncResult();
            foreach (var planId in planIds)
            {
                await SyncPlanAsync(graph, planId, result, ct);
            }

            result.StatusMessage =
                $"Synced {result.PlansUpserted} plan(s), {result.BucketsUpserted} bucket(s), {result.TasksUpserted} task(s).";
            await TryCaptureSnapshotAsync(ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Planner Graph sync failed.");
            if (_settings.SeedDemoData)
            {
                var seeded = await SeedDemoAsync(ct);
                seeded.Errors.Add(ex.Message);
                seeded.StatusMessage = "Graph Planner sync failed — demo data loaded. " +
                                       "Grant Tasks.Read.All + Group.Read.All (app) and set Planner:GroupIds/PlanIds. " +
                                       seeded.StatusMessage;
                return seeded;
            }

            return new PlannerSyncResult
            {
                StatusMessage = "Planner sync failed.",
                Errors = [ex.Message]
            };
        }
    }

    public async Task<PlannerHealthSummary> GetHealthSummaryAsync(CancellationToken ct = default)
    {
        var tasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);
        var completed = tasks.Count(t => t.PercentComplete >= 100 || t.Status == "Completed");
        var inProgress = tasks.Count(t => t.PercentComplete is > 0 and < 100);
        var notStarted = tasks.Count - completed - inProgress;
        var delayed = tasks.Count(t => t.IsDelayed);
        var total = tasks.Count;
        var completion = total == 0 ? 0 : Math.Round(100.0 * completed / total, 1);

        var risk = delayed switch
        {
            0 => "Low",
            <= 3 => "Medium",
            _ => "High"
        };

        return new PlannerHealthSummary
        {
            TotalTasks = total,
            Completed = completed,
            InProgress = inProgress,
            NotStarted = Math.Max(0, notStarted),
            Delayed = delayed,
            CompletionPercent = completion,
            RiskLevel = risk,
            PlanCount = await _db.PlannerPlans.CountAsync(ct),
            LastSyncAt = await _db.PlannerPlans.MaxAsync(p => (DateTimeOffset?)p.LastSyncAt, ct),
            StatusMessage = total == 0
                ? "No Planner tasks yet — run Sync or enable SeedDemoData."
                : $"Overall health: {(risk == "Low" ? "Good" : risk == "Medium" ? "Watch" : "At risk")}."
        };
    }

    public async Task<List<PlannerPlanListItem>> ListPlansAsync(CancellationToken ct = default)
    {
        return await _db.PlannerPlans.AsNoTracking()
            .OrderBy(p => p.Title)
            .Select(p => new PlannerPlanListItem
            {
                Id = p.Id,
                GraphPlanId = p.GraphPlanId,
                Title = p.Title,
                TaskCount = p.Tasks.Count,
                LastSyncAt = p.LastSyncAt
            })
            .ToListAsync(ct);
    }

    public async Task<List<PlannerTaskListItem>> ListTasksAsync(
        string? planId = null,
        bool delayedOnly = false,
        string? assigneeContains = null,
        DateTimeOffset? dueFrom = null,
        DateTimeOffset? dueTo = null,
        CancellationToken ct = default)
    {
        var query = _db.PlannerTasks.AsNoTracking().Include(t => t.Plan).AsQueryable();

        if (!string.IsNullOrWhiteSpace(planId) && Guid.TryParse(planId, out var pid))
        {
            query = query.Where(t => t.PlanId == pid);
        }

        if (delayedOnly)
        {
            query = query.Where(t => t.IsDelayed);
        }

        if (!string.IsNullOrWhiteSpace(assigneeContains))
        {
            var needle = assigneeContains.Trim();
            query = query.Where(t => t.AssignedUsers != null && t.AssignedUsers.Contains(needle));
        }

        if (dueFrom.HasValue)
        {
            var from = dueFrom.Value;
            query = query.Where(t => t.DueDate != null && t.DueDate >= from);
        }

        if (dueTo.HasValue)
        {
            var to = dueTo.Value;
            query = query.Where(t => t.DueDate != null && t.DueDate <= to);
        }

        return await query
            .OrderBy(t => t.DueDate ?? DateTimeOffset.MaxValue)
            .ThenBy(t => t.Title)
            .Select(t => new PlannerTaskListItem
            {
                Id = t.Id,
                GraphTaskId = t.GraphTaskId,
                Title = t.Title,
                PlanTitle = t.Plan != null ? t.Plan.Title : null,
                BucketName = t.BucketName,
                StartDate = t.StartDate,
                DueDate = t.DueDate,
                PercentComplete = t.PercentComplete,
                Status = t.Status,
                AssignedUsers = t.AssignedUsers,
                IsDelayed = t.IsDelayed
            })
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<List<PlannerWorkloadRow>> GetWorkloadAsync(CancellationToken ct = default)
    {
        var tasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);
        var groups = new Dictionary<string, List<PlannerTaskItem>>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in tasks)
        {
            var raw = string.IsNullOrWhiteSpace(t.AssignedUsers) ? "Unassigned" : t.AssignedUsers!;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = string.IsNullOrWhiteSpace(part) ? "Unassigned" : part;
                if (!groups.TryGetValue(key, out var list))
                {
                    list = [];
                    groups[key] = list;
                }

                list.Add(t);
            }
        }

        return groups
            .Select(g =>
            {
                var list = g.Value;
                var completed = list.Count(t => t.PercentComplete >= 100);
                var inProgress = list.Count(t => t.PercentComplete is > 0 and < 100);
                return new PlannerWorkloadRow
                {
                    Assignee = g.Key,
                    TotalTasks = list.Count,
                    Completed = completed,
                    InProgress = inProgress,
                    Delayed = list.Count(t => t.IsDelayed),
                    AvgPercentComplete = list.Count == 0
                        ? 0
                        : Math.Round(list.Average(t => t.PercentComplete), 1)
                };
            })
            .OrderByDescending(r => r.Delayed)
            .ThenByDescending(r => r.TotalTasks)
            .ThenBy(r => r.Assignee)
            .ToList();
    }

    private async Task<List<string>> ResolvePlanIdsAsync(GraphServiceClient graph, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in _settings.PlanIds.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            ids.Add(id.Trim());
        }

        foreach (var groupId in _settings.GroupIds.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            var plans = await graph.Groups[groupId.Trim()].Planner.Plans.GetAsync(cancellationToken: ct);
            foreach (var plan in plans?.Value ?? [])
            {
                if (!string.IsNullOrWhiteSpace(plan.Id))
                {
                    ids.Add(plan.Id);
                }
            }
        }

        return ids.ToList();
    }

    private async Task SyncPlanAsync(
        GraphServiceClient graph,
        string graphPlanId,
        PlannerSyncResult result,
        CancellationToken ct)
    {
        var planDto = await graph.Planner.Plans[graphPlanId].GetAsync(cancellationToken: ct);
        if (planDto?.Id is null)
        {
            result.Errors.Add($"Plan {graphPlanId} not found.");
            return;
        }

        var plan = await _db.PlannerPlans.FirstOrDefaultAsync(p => p.GraphPlanId == graphPlanId, ct)
                   ?? new PlanEntity { GraphPlanId = graphPlanId, Title = planDto.Title ?? "Untitled plan" };

        plan.Title = planDto.Title ?? plan.Title;
        plan.GraphGroupId = planDto.Container?.ContainerId ?? plan.GraphGroupId;
        plan.LastSyncAt = DateTimeOffset.UtcNow;

        if (_db.Entry(plan).State == EntityState.Detached)
        {
            _db.PlannerPlans.Add(plan);
        }

        await _db.SaveChangesAsync(ct);
        result.PlansUpserted++;

        var buckets = await graph.Planner.Plans[graphPlanId].Buckets.GetAsync(cancellationToken: ct);
        var bucketNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var b in buckets?.Value ?? [])
        {
            if (string.IsNullOrWhiteSpace(b.Id)) continue;

            var bucket = await _db.PlannerBuckets
                .FirstOrDefaultAsync(x => x.GraphBucketId == b.Id, ct)
                ?? new BucketEntity
                {
                    PlanId = plan.Id,
                    GraphBucketId = b.Id,
                    Name = b.Name ?? "Bucket"
                };

            bucket.PlanId = plan.Id;
            bucket.Name = b.Name ?? bucket.Name;
            bucket.OrderHint = int.TryParse(b.OrderHint?.Trim(), out var oh) ? oh : 0;
            bucketNames[b.Id] = bucket.Name;

            if (_db.Entry(bucket).State == EntityState.Detached)
            {
                _db.PlannerBuckets.Add(bucket);
            }

            result.BucketsUpserted++;
        }

        await _db.SaveChangesAsync(ct);

        var tasks = await graph.Planner.Plans[graphPlanId].Tasks.GetAsync(cancellationToken: ct);
        foreach (var t in tasks?.Value ?? [])
        {
            if (string.IsNullOrWhiteSpace(t.Id)) continue;

            var row = await _db.PlannerTasks.FirstOrDefaultAsync(x => x.GraphTaskId == t.Id, ct)
                      ?? new TaskEntity
                      {
                          PlanId = plan.Id,
                          GraphTaskId = t.Id,
                          Title = t.Title ?? "Untitled"
                      };

            row.PlanId = plan.Id;
            row.Title = t.Title ?? row.Title;
            row.GraphBucketId = t.BucketId;
            row.BucketName = t.BucketId is not null && bucketNames.TryGetValue(t.BucketId, out var bn)
                ? bn
                : row.BucketName;
            row.PercentComplete = t.PercentComplete ?? 0;
            row.StartDate = t.StartDateTime;
            row.DueDate = t.DueDateTime;
            row.GraphCreatedAt = t.CreatedDateTime;
            row.GraphModifiedAt = t.CompletedDateTime ?? t.CreatedDateTime;
            row.AssignedUsers = ExtractAssigneeKeys(t);
            row.Status = DeriveStatus(row.PercentComplete);
            row.IsDelayed = ComputeDelayed(row);
            row.LastSyncAt = DateTimeOffset.UtcNow;

            if (_db.Entry(row).State == EntityState.Detached)
            {
                _db.PlannerTasks.Add(row);
            }

            result.TasksUpserted++;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task<PlannerSyncResult> SeedDemoAsync(CancellationToken ct)
    {
        const string demoPlanGraphId = "demo-wealth-copilot-plan";

        var plan = await _db.PlannerPlans.FirstOrDefaultAsync(p => p.GraphPlanId == demoPlanGraphId, ct);
        if (plan is null)
        {
            plan = new PlanEntity
            {
                GraphPlanId = demoPlanGraphId,
                Title = "Wealth Copilot Delivery",
                OwnerName = "BD Team",
                LastSyncAt = DateTimeOffset.UtcNow
            };
            _db.PlannerPlans.Add(plan);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            plan.LastSyncAt = DateTimeOffset.UtcNow;
        }

        var existing = await _db.PlannerTasks.CountAsync(t => t.PlanId == plan.Id, ct);
        if (existing > 0)
        {
            // Refresh delayed flags
            var tasks = await _db.PlannerTasks.Where(t => t.PlanId == plan.Id).ToListAsync(ct);
            foreach (var t in tasks)
            {
                t.IsDelayed = ComputeDelayed(t);
                t.Status = DeriveStatus(t.PercentComplete);
                t.LastSyncAt = DateTimeOffset.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            await TryCaptureSnapshotAsync(ct);
            return new PlannerSyncResult
            {
                UsedDemoSeed = true,
                PlansUpserted = 1,
                TasksUpserted = tasks.Count,
                StatusMessage = $"Demo plan already present ({tasks.Count} tasks) — refreshed delay flags."
            };
        }

        var buckets = new[]
        {
            ("demo-b-backlog", "Backlog", 1),
            ("demo-b-progress", "In Progress", 2),
            ("demo-b-test", "Testing", 3),
            ("demo-b-done", "Completed", 4)
        };

        foreach (var (id, name, order) in buckets)
        {
            _db.PlannerBuckets.Add(new BucketEntity
            {
                PlanId = plan.Id,
                GraphBucketId = id,
                Name = name,
                OrderHint = order
            });
        }

        var today = DateTimeOffset.UtcNow.Date;
        var seedTasks = new[]
        {
            ("Requirements workshop", today.AddDays(-20), today.AddDays(-14), 100, "Completed", "demo-b-done", "Priya Sharma"),
            ("Solution design", today.AddDays(-13), today.AddDays(-7), 100, "Completed", "demo-b-done", "Alex Chen"),
            ("Teams bot channel", today.AddDays(-6), today.AddDays(2), 75, "In Progress", "demo-b-progress", "Ramchandra Pujari"),
            ("SharePoint / Graph sync", today.AddDays(-10), today.AddDays(-1), 40, "In Progress", "demo-b-progress", "Ramchandra Pujari"),
            ("RFP generator hardening", today.AddDays(-5), today.AddDays(5), 50, "In Progress", "demo-b-progress", "Priya Sharma"),
            ("Planner intelligence Phase 1", today.AddDays(-2), today.AddDays(8), 25, "In Progress", "demo-b-progress", "Alex Chen"),
            ("KYC integration spike", today.AddDays(1), today.AddDays(14), 0, "NotStarted", "demo-b-backlog", "Priya Sharma"),
            ("Document management UX", today.AddDays(3), today.AddDays(18), 0, "NotStarted", "demo-b-backlog", "Alex Chen"),
            ("UAT & security review", today.AddDays(10), today.AddDays(24), 0, "NotStarted", "demo-b-test", "QA Guild"),
            ("Go-live checklist", today.AddDays(20), today.AddDays(30), 0, "NotStarted", "demo-b-test", "BD Team")
        };

        var n = 0;
        foreach (var (title, start, due, pct, status, bucketId, assignee) in seedTasks)
        {
            n++;
            var row = new TaskEntity
            {
                PlanId = plan.Id,
                GraphTaskId = $"demo-task-{n}",
                Title = title,
                StartDate = new DateTimeOffset(start, TimeSpan.Zero),
                DueDate = new DateTimeOffset(due, TimeSpan.Zero),
                PercentComplete = pct,
                Status = status,
                GraphBucketId = bucketId,
                BucketName = buckets.First(b => b.Item1 == bucketId).Item2,
                AssignedUsers = assignee,
                LastSyncAt = DateTimeOffset.UtcNow
            };
            row.IsDelayed = ComputeDelayed(row);
            _db.PlannerTasks.Add(row);
        }

        await _db.SaveChangesAsync(ct);
        await TryCaptureSnapshotAsync(ct);

        return new PlannerSyncResult
        {
            UsedDemoSeed = true,
            PlansUpserted = 1,
            BucketsUpserted = buckets.Length,
            TasksUpserted = seedTasks.Length,
            StatusMessage = $"Seeded demo plan “{plan.Title}” with {seedTasks.Length} tasks."
        };
    }

    private async Task TryCaptureSnapshotAsync(CancellationToken ct)
    {
        try
        {
            await _snapshots.CaptureDailySnapshotAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Planner daily snapshot capture failed.");
        }
    }

    private static string? ExtractAssigneeKeys(GraphPlannerTask task)
    {
        if (task.Assignments?.AdditionalData is { Count: > 0 } data)
        {
            return string.Join(", ", data.Keys.Take(8));
        }

        return null;
    }

    private static string DeriveStatus(int percentComplete) =>
        percentComplete >= 100 ? "Completed" : percentComplete > 0 ? "InProgress" : "NotStarted";

    private static bool ComputeDelayed(TaskEntity t) =>
        t.PercentComplete < 100
        && t.DueDate.HasValue
        && t.DueDate.Value.UtcDateTime.Date < DateTime.UtcNow.Date;
}
