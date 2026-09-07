using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

public sealed class PipelineCapacityService : IPipelineCapacityService
{
    private static readonly HashSet<string> LateStages = new(StringComparer.OrdinalIgnoreCase)
    {
        "Proposal", "Negotiation", "Closing", "Finalist", "Shortlist", "Contract"
    };

    private readonly BdCopilotDbContext _db;
    private readonly IPlannerCapacityService _capacity;
    private readonly LibraryStalenessSettings _staleness;

    public PipelineCapacityService(
        BdCopilotDbContext db,
        IPlannerCapacityService capacity,
        IOptions<LibraryStalenessSettings> staleness)
    {
        _db = db;
        _capacity = capacity;
        _staleness = staleness.Value;
    }

    public async Task<PipelineCapacityView> GetPipelineCapacityAsync(CancellationToken ct = default)
    {
        var pursuits = await _db.Opportunities.AsNoTracking()
            .Where(o => o.Outcome == null || o.Outcome == "Open")
            .Where(o => o.Stage != "ClosedWon" && o.Stage != "ClosedLost")
            .OrderBy(o => o.Deadline ?? DateTimeOffset.MaxValue)
            .ToListAsync(ct);

        var heatmap = await _capacity.GetHeatmapAsync(6, ct);
        var assigneeLoad = heatmap.Cells
            .GroupBy(c => c.Assignee, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Max(c => c.FteLoad),
                StringComparer.OrdinalIgnoreCase);

        var rows = pursuits.Select(o =>
        {
            var owner = o.OwnerDisplayName ?? o.OwnerUserObjectId ?? "Unassigned";
            var load = assigneeLoad.TryGetValue(owner, out var fte) ? fte : 0;
            var lateStage = LateStages.Contains(o.Stage ?? "");
            var days = o.Deadline.HasValue
                ? (int)Math.Ceiling((o.Deadline.Value - DateTimeOffset.UtcNow).TotalDays)
                : 999;
            var gap = lateStage && load >= 0.85 && days <= 45;

            return new PipelineOpportunityRow
            {
                OpportunityId = o.Id,
                Name = o.Name,
                Client = o.Client,
                Stage = o.Stage ?? "Lead",
                OwnerDisplayName = o.OwnerDisplayName,
                Deadline = o.Deadline,
                DaysToDeadline = days,
                HasStaffingGap = gap
            };
        }).ToList();

        var gaps = rows
            .Where(r => r.HasStaffingGap)
            .Select(r =>
            {
                var owner = r.OwnerDisplayName ?? "Unassigned";
                var load = assigneeLoad.TryGetValue(owner, out var fte) ? fte : 0;
                return new PipelineStaffingGap
                {
                    Assignee = owner,
                    OpportunityName = r.Name,
                    Client = r.Client,
                    Stage = r.Stage,
                    Deadline = r.Deadline,
                    CurrentFteLoad = load,
                    GapLevel = load >= 1.1 ? "Critical" : "High",
                    Rationale =
                        $"{owner} at {load:P0} FTE with '{r.Name}' in {r.Stage} " +
                        $"({r.DaysToDeadline}d to deadline) — staffing gap before close."
                };
            })
            .ToList();

        var gapCount = gaps.Count;
        return new PipelineCapacityView
        {
            OpenPursuits = rows,
            Gaps = gaps,
            Summary = $"{rows.Count} open pursuit(s) · {gapCount} staffing gap(s) flagged.",
            Guidance = gapCount > 0
                ? "Rebalance Planner workload or add pursuit support before late-stage deadlines."
                : "Pipeline and capacity are aligned for the next 6 weeks."
        };
    }

    public async Task<List<PursuitDeadlineAlert>> GetPursuitDeadlineAlertsAsync(
        int withinDays = 14,
        CancellationToken ct = default)
    {
        withinDays = Math.Clamp(withinDays, 1, 90);
        var cutoff = DateTimeOffset.UtcNow.AddDays(withinDays);

        var rows = await _db.Opportunities.AsNoTracking()
            .Where(o => o.Deadline != null && o.Deadline <= cutoff)
            .Where(o => o.Outcome == null || o.Outcome == "Open")
            .Where(o => o.Stage != "ClosedWon" && o.Stage != "ClosedLost")
            .OrderBy(o => o.Deadline)
            .ToListAsync(ct);

        return rows.Select(o => new PursuitDeadlineAlert
        {
            OpportunityId = o.Id,
            Name = o.Name,
            Client = o.Client,
            Deadline = o.Deadline!.Value,
            DaysRemaining = Math.Max(0, (int)Math.Ceiling((o.Deadline!.Value - DateTimeOffset.UtcNow).TotalDays)),
            Stage = o.Stage ?? "Lead",
            OwnerDisplayName = o.OwnerDisplayName
        }).ToList();
    }

    public async Task<List<StaleDocumentAlert>> GetStaleDocumentAlertsAsync(CancellationToken ct = default)
    {
        var months = Math.Max(1, _staleness.StaleAfterMonths);
        var cutoff = DateTime.UtcNow.AddMonths(-months);

        var docs = await _db.Documents.AsNoTracking()
            .Where(d => d.ModifiedDate < cutoff)
            .OrderBy(d => d.ModifiedDate)
            .Take(20)
            .ToListAsync(ct);

        return docs.Select(d => new StaleDocumentAlert
        {
            DocumentId = d.DocumentId,
            FileName = d.FileName,
            MonthsSinceModified = Math.Max(1, (int)((DateTime.UtcNow - d.ModifiedDate).TotalDays / 30)),
            CorpusSource = d.TeamsChannel
        }).ToList();
    }
}
