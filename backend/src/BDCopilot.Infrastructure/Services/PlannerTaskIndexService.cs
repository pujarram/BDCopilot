using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

/// <summary>Indexes Planner task title + description as a third RAG corpus alongside Local and Online docs.</summary>
public sealed class PlannerTaskIndexService : IPlannerTaskIndexService
{
    private readonly BdCopilotDbContext _db;
    private readonly IDocumentSyncService _documentSync;
    private readonly ILogger<PlannerTaskIndexService> _logger;

    public PlannerTaskIndexService(
        BdCopilotDbContext db,
        IDocumentSyncService documentSync,
        ILogger<PlannerTaskIndexService> logger)
    {
        _db = db;
        _documentSync = documentSync;
        _logger = logger;
    }

    public async Task<int> IndexAllTasksAsync(CancellationToken ct = default)
    {
        var tasks = await _db.PlannerTasks
            .AsNoTracking()
            .Include(t => t.Plan)
            .ToListAsync(ct);

        var indexed = 0;
        foreach (var task in tasks)
        {
            await IndexTaskAsync(task, ct);
            indexed++;
        }

        _logger.LogInformation("Indexed {Count} Planner task(s) into RAG corpus.", indexed);
        return indexed;
    }

    private async Task IndexTaskAsync(PlannerTaskItem task, CancellationToken ct)
    {
        var driveItemId = task.GraphTaskId;
        var doc = await _db.Documents
            .FirstOrDefaultAsync(d =>
                d.GraphDriveId == CorpusSources.PlannerDriveId &&
                d.GraphDriveItemId == driveItemId, ct);

        if (doc is null)
        {
            doc = new Document
            {
                DocumentId = Guid.NewGuid(),
                FileName = $"[Planner] {task.Title}",
                FileType = "planner-task",
                GraphDriveId = CorpusSources.PlannerDriveId,
                GraphDriveItemId = driveItemId,
                SharePointUrl = $"planner://task/{driveItemId}",
                TeamsChannel = task.Plan?.Title ?? "Planner",
                IndexStatus = IndexStatus.Pending
            };
            _db.Documents.Add(doc);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            doc.FileName = $"[Planner] {task.Title}";
            doc.TeamsChannel = task.Plan?.Title ?? doc.TeamsChannel;
        }

        var body = BuildTaskText(task);
        var sections = new List<(string Text, string? Locator)>
        {
            (body, $"Task · {task.BucketName ?? "General"}")
        };

        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            sections.Add((task.Description.Trim(), "Description"));
        }

        await _documentSync.ChunkAndEmbedAsync(doc, sections, ct);
    }

    private static string BuildTaskText(PlannerTaskItem task)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Planner task: {task.Title}");
        if (!string.IsNullOrWhiteSpace(task.Plan?.Title))
        {
            sb.AppendLine($"Plan: {task.Plan.Title}");
        }

        if (!string.IsNullOrWhiteSpace(task.BucketName))
        {
            sb.AppendLine($"Bucket: {task.BucketName}");
        }

        sb.AppendLine($"Status: {task.Status} · {task.PercentComplete}% complete");
        if (!string.IsNullOrWhiteSpace(task.AssignedUsers))
        {
            sb.AppendLine($"Owners: {task.AssignedUsers}");
        }

        if (task.DueDate.HasValue)
        {
            sb.AppendLine($"Due: {task.DueDate.Value:yyyy-MM-dd}");
        }

        if (task.IsDelayed)
        {
            sb.AppendLine("Delivery status: DELAYED — overdue and incomplete.");
        }

        if (!string.IsNullOrWhiteSpace(task.Description))
        {
            sb.AppendLine();
            sb.AppendLine(task.Description.Trim());
        }

        return sb.ToString().Trim();
    }
}
