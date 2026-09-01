using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface IPlannerSyncService
{
    Task<PlannerSyncResult> SyncAsync(CancellationToken ct = default);

    Task<PlannerHealthSummary> GetHealthSummaryAsync(CancellationToken ct = default);

    Task<List<PlannerPlanListItem>> ListPlansAsync(CancellationToken ct = default);

    Task<List<PlannerTaskListItem>> ListTasksAsync(
        string? planId = null,
        bool delayedOnly = false,
        string? assigneeContains = null,
        DateTimeOffset? dueFrom = null,
        DateTimeOffset? dueTo = null,
        CancellationToken ct = default);

    Task<List<PlannerWorkloadRow>> GetWorkloadAsync(CancellationToken ct = default);
}
