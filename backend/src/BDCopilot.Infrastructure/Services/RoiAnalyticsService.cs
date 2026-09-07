using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

public sealed class RoiAnalyticsService : IRoiAnalyticsService
{
    private const double MinutesSavedPerDoc = 45;
    private const double CostPer1kTokensUsd = 0.002;

    private readonly BdCopilotDbContext _db;

    public RoiAnalyticsService(BdCopilotDbContext db) => _db = db;

    public async Task<RoiDashboardSummary> GetRoiAsync(CancellationToken ct = default)
    {
        var since30 = DateTimeOffset.UtcNow.AddDays(-30);
        var genOps = new[] { "Rfp", "BusinessCase", "Proposal", "Competitive" };

        var tokens = await _db.TokenUsageRecords.Where(t => t.CreatedAt >= since30).ToListAsync(ct);
        var genRows = tokens.Where(t => genOps.Contains(t.Operation, StringComparer.OrdinalIgnoreCase)).ToList();

        var rfpAll = await _db.RfpDocuments.AsNoTracking().ToListAsync(ct);
        var wins = rfpAll.Count(r => r.Outcome == "Win");
        var losses = rfpAll.Count(r => r.Outcome == "Loss");
        var open = rfpAll.Count(r => r.Outcome == "Open" || string.IsNullOrWhiteSpace(r.Outcome));
        var decided = wins + losses;
        var winRate = decided == 0 ? 0 : Math.Round(100.0 * wins / decided, 1);

        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var thisMonth = genRows.Count(t => t.CreatedAt >= monthStart);

        var byMonth = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var start = monthStart.AddMonths(-i);
                var end = start.AddMonths(1);
                return new MonthlyGenerationPoint
                {
                    Month = start.ToString("yyyy-MM"),
                    Count = genRows.Count(t => t.CreatedAt >= start && t.CreatedAt < end)
                        + rfpAll.Count(r => r.CreatedAt >= start && r.CreatedAt < end &&
                                            !genRows.Any(g => g.CreatedAt >= start && g.CreatedAt < end))
                };
            })
            .OrderBy(p => p.Month)
            .ToList();

        // Prefer RFP history counts when token tracker is empty (demo).
        if (byMonth.All(p => p.Count == 0) && rfpAll.Count > 0)
        {
            byMonth = Enumerable.Range(0, 6)
                .Select(i =>
                {
                    var start = monthStart.AddMonths(-i);
                    var end = start.AddMonths(1);
                    return new MonthlyGenerationPoint
                    {
                        Month = start.ToString("yyyy-MM"),
                        Count = rfpAll.Count(r => r.CreatedAt >= start && r.CreatedAt < end)
                    };
                })
                .OrderBy(p => p.Month)
                .ToList();
            thisMonth = rfpAll.Count(r => r.CreatedAt >= monthStart);
        }

        var genCount30 = Math.Max(genRows.Count, rfpAll.Count(r => r.CreatedAt >= since30));
        var hoursSaved = Math.Round(genCount30 * MinutesSavedPerDoc / 60.0, 1);

        var trend = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var start = monthStart.AddMonths(-i);
                var end = start.AddMonths(1);
                var w = rfpAll.Count(r => r.Outcome == "Win" && (r.OutcomeTaggedAt ?? r.CreatedAt) >= start
                                          && (r.OutcomeTaggedAt ?? r.CreatedAt) < end);
                var l = rfpAll.Count(r => r.Outcome == "Loss" && (r.OutcomeTaggedAt ?? r.CreatedAt) >= start
                                          && (r.OutcomeTaggedAt ?? r.CreatedAt) < end);
                var d = w + l;
                return new WinRateTrendPoint
                {
                    Month = start.ToString("yyyy-MM"),
                    Wins = w,
                    Losses = l,
                    WinRatePercent = d == 0 ? 0 : Math.Round(100.0 * w / d, 1)
                };
            })
            .OrderBy(p => p.Month)
            .ToList();

        return new RoiDashboardSummary
        {
            MinutesSavedPerDocument = MinutesSavedPerDoc,
            HoursSavedTotal = hoursSaved,
            GenerationsThisMonth = thisMonth,
            GenerationsLast30Days = genCount30,
            GenerationsByMonth = byMonth,
            WinRatePercent = winRate,
            Wins = wins,
            Losses = losses,
            OpenOutcomes = open,
            WinRateTrend = trend,
            TokensLast30Days = tokens.Sum(t => (long)t.TotalTokens),
            RequestsLast30Days = tokens.Count,
            Guidance =
                "ROI assumes ~45 minutes saved per generated document vs blank-page drafting. " +
                "Win rate uses RFP outcome tags (Win/Loss). Tag past RFPs to improve the trend."
        };
    }

    public async Task<CustomerUsageSummary> GetCustomerUsageAsync(CancellationToken ct = default)
    {
        var since30 = DateTimeOffset.UtcNow.AddDays(-30);
        var tokens = await _db.TokenUsageRecords.Where(t => t.CreatedAt >= since30).ToListAsync(ct);
        var genOps = new[] { "Rfp", "BusinessCase", "Proposal", "Competitive", "Chat" };
        var generations = tokens.Count(t =>
            t.Operation is "Rfp" or "BusinessCase" or "Proposal" or "Competitive");

        var byOp = tokens
            .GroupBy(t => t.Operation)
            .Select(g => new OperationUsageRow
            {
                Operation = g.Key,
                RequestCount = g.Count(),
                TotalTokens = g.Sum(x => (long)x.TotalTokens)
            })
            .OrderByDescending(r => r.TotalTokens)
            .ToList();

        var top = tokens
            .GroupBy(t => new { t.TeamId, t.Initiative })
            .Select(g => new TeamTokenCostRow
            {
                TeamId = g.Key.TeamId,
                Initiative = g.Key.Initiative,
                TotalTokens = g.Sum(x => (long)x.TotalTokens),
                RequestCount = g.Count()
            })
            .OrderByDescending(r => r.TotalTokens)
            .Take(8)
            .ToList();

        var totalTokens = tokens.Sum(t => (long)t.TotalTokens);
        return new CustomerUsageSummary
        {
            TokensLast30Days = totalTokens,
            RequestsLast30Days = tokens.Count,
            GenerationsLast30Days = generations,
            CostPer1kTokensUsd = CostPer1kTokensUsd,
            EstimatedCostUsd = Math.Round(totalTokens / 1000.0 * CostPer1kTokensUsd, 2),
            TopConsumers = top,
            ByOperation = byOp,
            Guidance =
                "Customer-facing usage view (30 days). Internal App Insights remains the source of truth for latency SLOs; " +
                "this panel is for cost conversations with the tenant."
        };
    }
}
