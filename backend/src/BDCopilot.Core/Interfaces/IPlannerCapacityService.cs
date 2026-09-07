using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface IPlannerCapacityService
{
    Task<PlannerCapacityHeatmap> GetHeatmapAsync(int weeks = 6, CancellationToken ct = default);
}
