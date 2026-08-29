using BDCopilot.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace BDCopilot.SyncJobs.Jobs;

/// <summary>
/// Hangfire recurring job wrapper around <see cref="IDocumentSyncService"/>. Kept as a thin
/// class so Hangfire's serializer only ever has to store a type name + method name, not any
/// captured state — see HangfireJobScheduler for how it's registered on a schedule.
/// </summary>
public class DocumentSyncJob
{
    private readonly IDocumentSyncService _syncService;
    private readonly ILogger<DocumentSyncJob> _logger;

    public DocumentSyncJob(IDocumentSyncService syncService, ILogger<DocumentSyncJob> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Document sync job starting.");
        try
        {
            await _syncService.SyncAsync();
            _logger.LogInformation("Document sync job finished.");
        }
        catch (Exception ex)
        {
            // Let Hangfire's built-in retry policy handle transient failures (Graph throttling,
            // a momentarily unreachable Postgres) — rethrow rather than swallowing here.
            _logger.LogError(ex, "Document sync job failed.");
            throw;
        }
    }
}
