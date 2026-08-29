using BDCopilot.SyncJobs.Jobs;
using Hangfire;

namespace BDCopilot.SyncJobs;

public static class HangfireJobScheduler
{
    /// <summary>
    /// Registers the recurring document-sync job. Every 5 minutes matches the "delta queries,
    /// not full rescans" design decision in the architecture doc — because Graph delta queries
    /// only return what changed, a short interval is cheap once Phase 1 lands, unlike a nightly
    /// full crawl would be.
    /// </summary>
    public static void RegisterRecurringJobs(this IRecurringJobManager recurringJobs)
    {
        recurringJobs.AddOrUpdate<DocumentSyncJob>(
            recurringJobId: "document-sync",
            methodCall: job => job.RunAsync(),
            cronExpression: "*/5 * * * *");
    }
}
