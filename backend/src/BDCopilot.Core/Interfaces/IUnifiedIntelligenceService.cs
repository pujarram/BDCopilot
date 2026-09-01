using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface IPlannerSnapshotService
{
    Task CaptureDailySnapshotAsync(CancellationToken ct = default);

    Task<List<PlannerBurndownPoint>> GetBurndownAsync(int days = 30, CancellationToken ct = default);
}

public interface IUnifiedIntelligenceService
{
    Task<UnifiedIntelligenceResponse> QueryAsync(
        UnifiedIntelligenceRequest request,
        CancellationToken ct = default);
}
