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
        var exists = await _db.PlannerTaskSnapshots.AsNoTracking()
            .AnyAsync(s => s.SnapshotDate == today, ct);
        if (exists)
        {
            return;
        }

        var tasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);
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

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Planner snapshot captured for {Date}: tasks={Total}, delayed={Delayed}, health={Health}",
            today, total, delayed, health);
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
}
