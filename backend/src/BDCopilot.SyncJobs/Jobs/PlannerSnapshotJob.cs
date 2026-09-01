using BDCopilot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BDCopilot.SyncJobs.Jobs;

public class PlannerSnapshotJob
{
    private readonly IPlannerSnapshotService _snapshots;
    private readonly ILogger<PlannerSnapshotJob> _logger;

    public PlannerSnapshotJob(IPlannerSnapshotService snapshots, ILogger<PlannerSnapshotJob> logger)
    {
        _snapshots = snapshots;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        await _snapshots.CaptureDailySnapshotAsync();
        _logger.LogInformation("Planner snapshot job finished.");
    }
}
