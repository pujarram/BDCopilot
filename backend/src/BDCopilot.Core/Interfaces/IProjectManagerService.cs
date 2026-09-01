using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

/// <summary>Phase 4 — health score, delay prediction, staffing, stakeholder reports.</summary>
public interface IProjectManagerService
{
    Task<ProjectManagerInsight> GetInsightsAsync(CancellationToken ct = default);

    Task<StakeholderWeeklyReport> GenerateStakeholderReportAsync(
        bool useLlmNarrative = true,
        string? userObjectId = null,
        CancellationToken ct = default);

    Task<Stream> ExportStakeholderReportDocxAsync(
        bool useLlmNarrative = true,
        string? userObjectId = null,
        CancellationToken ct = default);
}
