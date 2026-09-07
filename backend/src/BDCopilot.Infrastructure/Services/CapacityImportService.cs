using System.Globalization;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

public sealed class CapacityImportService : ICapacityImportService
{
    private readonly BdCopilotDbContext _db;

    public CapacityImportService(BdCopilotDbContext db) => _db = db;

    public async Task<CapacityImportResult> ImportCsvAsync(Stream csvStream, CancellationToken ct = default)
    {
        using var reader = new StreamReader(csvStream);
        var header = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(header))
        {
            return new CapacityImportResult { Summary = "Empty CSV file." };
        }

        var cols = ParseCsvLine(header);
        var assigneeIdx = IndexOf(cols, "assignee", "assigneename", "name", "email");
        var weekIdx = IndexOf(cols, "weekstart", "week", "week_start");
        var hoursIdx = IndexOf(cols, "capacityhours", "hours", "capacity", "fte_hours");
        var taskIdx = IndexOf(cols, "tasktitle", "task", "title");
        var taskHoursIdx = IndexOf(cols, "estimatedhours", "taskhours", "effort");

        if (assigneeIdx < 0)
        {
            return new CapacityImportResult
            {
                Summary = "CSV must include Assignee or AssigneeName column."
            };
        }

        var result = new CapacityImportResult();
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            result.RowsProcessed++;
            var parts = ParseCsvLine(line);
            if (parts.Count <= assigneeIdx) { result.Skipped++; continue; }

            var assignee = parts[assigneeIdx].Trim();
            if (string.IsNullOrWhiteSpace(assignee)) { result.Skipped++; continue; }

            if (weekIdx >= 0 && weekIdx < parts.Count && hoursIdx >= 0 && hoursIdx < parts.Count
                && TryParseWeek(parts[weekIdx], out var weekStart)
                && double.TryParse(parts[hoursIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var capHours))
            {
                var existing = await _db.PlannerCapacityOverrides
                    .FirstOrDefaultAsync(o => o.AssigneeKey == assignee && o.WeekStart == weekStart, ct);
                if (existing is null)
                {
                    _db.PlannerCapacityOverrides.Add(new PlannerCapacityOverride
                    {
                        AssigneeKey = assignee,
                        WeekStart = weekStart,
                        CapacityHours = capHours,
                        Source = "csv-import"
                    });
                }
                else
                {
                    existing.CapacityHours = capHours;
                    existing.ImportedAt = DateTimeOffset.UtcNow;
                }

                result.OverridesUpserted++;
            }

            if (taskIdx >= 0 && taskHoursIdx >= 0 && taskIdx < parts.Count && taskHoursIdx < parts.Count
                && double.TryParse(parts[taskHoursIdx], NumberStyles.Float, CultureInfo.InvariantCulture, out var taskHours))
            {
                var title = parts[taskIdx].Trim();
                var tasks = await _db.PlannerTasks
                    .Where(t => t.Title.Contains(title) &&
                                (t.AssignedUsers != null && t.AssignedUsers.Contains(assignee)))
                    .ToListAsync(ct);
                foreach (var t in tasks)
                {
                    t.EstimatedHours = taskHours;
                    result.TaskHoursUpdated++;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        result.Summary =
            $"Processed {result.RowsProcessed} row(s): {result.OverridesUpserted} capacity override(s), " +
            $"{result.TaskHoursUpdated} task hour update(s), {result.Skipped} skipped.";
        return result;
    }

    private static int IndexOf(IReadOnlyList<string> cols, params string[] names)
    {
        for (var i = 0; i < cols.Count; i++)
        {
            var c = cols[i].Replace(" ", "").Replace("_", "").ToLowerInvariant();
            if (names.Any(n => c == n)) return i;
        }

        return -1;
    }

    private static bool TryParseWeek(string raw, out DateOnly weekStart)
    {
        weekStart = default;
        if (DateOnly.TryParse(raw, out weekStart)) return true;
        if (DateTime.TryParse(raw, out var dt))
        {
            weekStart = DateOnly.FromDateTime(dt);
            return true;
        }

        return false;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (ch == ',' && !inQuotes)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        parts.Add(current.ToString());
        return parts;
    }
}
