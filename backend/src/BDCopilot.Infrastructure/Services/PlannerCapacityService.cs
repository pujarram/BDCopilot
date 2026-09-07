using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

public sealed class PlannerCapacityService : IPlannerCapacityService
{
    private const double DefaultWeeklyCapacityHours = 40;

    private readonly BdCopilotDbContext _db;

    public PlannerCapacityService(BdCopilotDbContext db) => _db = db;

    public async Task<PlannerCapacityHeatmap> GetHeatmapAsync(int weeks = 6, CancellationToken ct = default)
    {
        weeks = Math.Clamp(weeks, 2, 12);
        var tasks = await _db.PlannerTasks.AsNoTracking()
            .Where(t => t.PercentComplete < 100)
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStarts = Enumerable.Range(0, weeks)
            .Select(i => StartOfWeek(today.AddDays(i * 7)))
            .ToList();

        var assignees = tasks
            .SelectMany(t => SplitAssignees(t.AssignedUsers))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(a => a)
            .ToList();

        if (assignees.Count == 0)
        {
            assignees.Add("Unassigned");
        }

        var cells = new List<PlannerCapacityCell>();
        foreach (var assignee in assignees)
        {
            foreach (var week in weekStarts)
            {
                var weekEnd = week.AddDays(6);
                var matching = tasks.Where(t =>
                    SplitAssignees(t.AssignedUsers).Any(a =>
                        a.Equals(assignee, StringComparison.OrdinalIgnoreCase))
                    && TaskFallsInWeek(t, week, weekEnd)).ToList();

                var hours = matching.Sum(t => RemainingHours(t));
                var weeklyCap = await GetWeeklyCapacityAsync(assignee, week, ct);
                var fte = hours / weeklyCap;
                cells.Add(new PlannerCapacityCell
                {
                    Assignee = assignee,
                    WeekStart = week,
                    EstimatedHours = Math.Round(hours, 1),
                    FteLoad = Math.Round(fte, 2),
                    OpenTasks = matching.Count,
                    HeatLevel = fte switch
                    {
                        >= 1.25 => "Critical",
                        >= 0.85 => "High",
                        >= 0.5 => "Medium",
                        _ => "Low"
                    }
                });
            }
        }

        var overloaded = cells.Count(c => c.HeatLevel is "High" or "Critical");
        return new PlannerCapacityHeatmap
        {
            Assignees = assignees,
            WeekStarts = weekStarts,
            Cells = cells,
            WeeklyCapacityHours = DefaultWeeklyCapacityHours,
            Guidance = overloaded > 0
                ? $"{overloaded} assignee-week cell(s) above 85% FTE — review heat map before committing new BD work."
                : "Capacity within normal range across the next weeks."
        };
    }

    private static IEnumerable<string> SplitAssignees(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield return "Unassigned";
            yield break;
        }

        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return string.IsNullOrWhiteSpace(part) ? "Unassigned" : part;
        }
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;
        var mondayOffset = dow == 0 ? -6 : 1 - dow;
        return date.AddDays(mondayOffset);
    }

    private static bool TaskFallsInWeek(PlannerTaskItem task, DateOnly weekStart, DateOnly weekEnd)
    {
        var anchor = task.DueDate?.UtcDateTime ?? task.StartDate?.UtcDateTime;
        if (!anchor.HasValue)
        {
            return weekStart == StartOfWeek(DateOnly.FromDateTime(DateTime.UtcNow));
        }

        var d = DateOnly.FromDateTime(anchor.Value);
        return d >= weekStart && d <= weekEnd;
    }

    private async Task<double> GetWeeklyCapacityAsync(string assignee, DateOnly weekStart, CancellationToken ct)
    {
        var row = await _db.PlannerCapacityOverrides.AsNoTracking()
            .FirstOrDefaultAsync(o =>
                o.AssigneeKey == assignee && o.WeekStart == weekStart, ct);
        return row?.CapacityHours > 0 ? row.CapacityHours : DefaultWeeklyCapacityHours;
    }

    private static double RemainingHours(PlannerTaskItem task)
    {
        var total = task.EstimatedHours > 0 ? task.EstimatedHours : 8;
        var remaining = total * (100 - Math.Clamp(task.PercentComplete, 0, 100)) / 100.0;
        return Math.Max(1, remaining);
    }
}
