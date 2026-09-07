namespace BDCopilot.Core.Interfaces;

public interface IPlannerTaskIndexService
{
    /// <summary>Embed all Planner tasks into the shared document_chunks index (GraphDriveId = planner-tasks).</summary>
    Task<int> IndexAllTasksAsync(CancellationToken ct = default);
}
