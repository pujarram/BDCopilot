using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public sealed class PlannerSnapshotService : IPlannerSnapshotService
{
    private readonly BdCopilotDbContext _db;
    private readonly ILogger<PlannerSnapshotService> _logger;

    public PlannerSnapshotService(BdCopilotDbContext db, ILogger<PlannerSnapshotService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task CaptureDailySnapshotAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var tasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);

        var aggregateExists = await _db.PlannerTaskSnapshots.AsNoTracking()
            .AnyAsync(s => s.SnapshotDate == today, ct);

        if (!aggregateExists)
        {
            var completed = tasks.Count(t => t.PercentComplete >= 100 || t.Status == "Completed");
            var inProgress = tasks.Count(t => t.PercentComplete is > 0 and < 100);
            var notStarted = Math.Max(0, tasks.Count - completed - inProgress);
            var delayed = tasks.Count(t => t.IsDelayed);
            var total = tasks.Count;
            var completion = total == 0 ? 0 : Math.Round(100.0 * completed / total, 1);
            var health = ProjectManagerService.ComputeHealthScore(tasks);

            _db.PlannerTaskSnapshots.Add(new PlannerTaskSnapshot
            {
                SnapshotDate = today,
                TotalTasks = total,
                Completed = completed,
                InProgress = inProgress,
                NotStarted = notStarted,
                Delayed = delayed,
                CompletionPercent = completion,
                HealthScore = health,
                CapturedAt = DateTimeOffset.UtcNow
            });
        }

        await CapturePerTaskStatesAsync(tasks, today, ct);
        await _db.SaveChangesAsync(ct);

        if (!aggregateExists)
        {
            _logger.LogInformation(
                "Planner snapshot captured for {Date}: tasks={Total}, delayed={Delayed}",
                today, tasks.Count, tasks.Count(t => t.IsDelayed));
        }
    }

    public async Task<List<PlannerBurndownPoint>> GetBurndownAsync(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 90);
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));

        var rows = await _db.PlannerTaskSnapshots.AsNoTracking()
            .Where(s => s.SnapshotDate >= from)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync(ct);

        return rows.Select(s => new PlannerBurndownPoint
        {
            Date = s.SnapshotDate,
            TotalTasks = s.TotalTasks,
            Completed = s.Completed,
            InProgress = s.InProgress,
            NotStarted = s.NotStarted,
            Delayed = s.Delayed,
            CompletionPercent = s.CompletionPercent,
            HealthScore = s.HealthScore
        }).ToList();
    }

    public async Task<List<PlannerStalledAlert>> GetStalledAlertsAsync(int days = 7, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 3, 30);
        var cutoff = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
        var tasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);
        var alerts = new List<PlannerStalledAlert>();

        foreach (var task in tasks.Where(t => t.PercentComplete < 100))
        {
            var history = await _db.PlannerTaskDailyStates.AsNoTracking()
                .Where(s => s.TaskId == task.Id && s.SnapshotDate >= cutoff)
                .OrderBy(s => s.SnapshotDate)
                .ToListAsync(ct);

            if (history.Count < days)
            {
                continue;
            }

            var firstPct = history.First().PercentComplete;
            if (history.All(h => h.PercentComplete == firstPct))
            {
                alerts.Add(new PlannerStalledAlert
                {
                    TaskId = task.Id,
                    GraphTaskId = task.GraphTaskId,
                    Title = task.Title,
                    AssignedUsers = task.AssignedUsers,
                    PercentComplete = firstPct,
                    StalledDays = history.Count,
                    LastProgressDate = history.First().SnapshotDate,
                    Message = $"Progress stalled at {firstPct}% for {history.Count} day(s) — owner: {task.AssignedUsers ?? "Unassigned"}"
                });
            }
        }

        return alerts
            .OrderByDescending(a => a.StalledDays)
            .ThenBy(a => a.Title)
            .ToList();
    }

    private async Task CapturePerTaskStatesAsync(
        IReadOnlyList<PlannerTaskItem> tasks,
        DateOnly today,
        CancellationToken ct)
    {
        foreach (var task in tasks)
        {
            var exists = await _db.PlannerTaskDailyStates
                .AnyAsync(s => s.TaskId == task.Id && s.SnapshotDate == today, ct);
            if (exists) continue;

            _db.PlannerTaskDailyStates.Add(new PlannerTaskDailyState
            {
                TaskId = task.Id,
                SnapshotDate = today,
                PercentComplete = task.PercentComplete,
                Status = task.Status
            });
        }

        await EnsureDemoStallHistoryAsync(ct);
    }

    /// <summary>Backfill demo history so stall alerts work without waiting 7 real days.</summary>
    private async Task EnsureDemoStallHistoryAsync(CancellationToken ct)
    {
        var stalledDemo = await _db.PlannerTasks
            .FirstOrDefaultAsync(t => t.GraphTaskId == "demo-task-4", ct);
        if (stalledDemo is null) return;

        var existing = await _db.PlannerTaskDailyStates
            .CountAsync(s => s.TaskId == stalledDemo.Id, ct);
        if (existing >= 7) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 7; i >= 1; i--)
        {
            var date = today.AddDays(-i);
            var has = await _db.PlannerTaskDailyStates
                .AnyAsync(s => s.TaskId == stalledDemo.Id && s.SnapshotDate == date, ct);
            if (has) continue;

            _db.PlannerTaskDailyStates.Add(new PlannerTaskDailyState
            {
                TaskId = stalledDemo.Id,
                SnapshotDate = date,
                PercentComplete = 40,
                Status = "InProgress"
            });
        }
    }
}
