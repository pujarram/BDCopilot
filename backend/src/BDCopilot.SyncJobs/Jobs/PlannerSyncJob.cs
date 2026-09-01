using BDCopilot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BDCopilot.SyncJobs.Jobs;

public class PlannerSyncJob
{
    private readonly IPlannerSyncService _planner;
    private readonly ILogger<PlannerSyncJob> _logger;

    public PlannerSyncJob(IPlannerSyncService planner, ILogger<PlannerSyncJob> logger)
    {
        _planner = planner;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var result = await _planner.SyncAsync();
        _logger.LogInformation("Planner sync: {Message}", result.StatusMessage);
    }
}
