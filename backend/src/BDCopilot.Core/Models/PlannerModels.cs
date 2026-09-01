namespace BDCopilot.Core.Models;

public class PlannerPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string GraphPlanId { get; set; }
    public string? GraphGroupId { get; set; }
    public required string Title { get; set; }
    public string? OwnerName { get; set; }
    public DateTimeOffset LastSyncAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<PlannerBucket> Buckets { get; set; } = new List<PlannerBucket>();
    public ICollection<PlannerTaskItem> Tasks { get; set; } = new List<PlannerTaskItem>();
}

public class PlannerBucket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public PlannerPlan? Plan { get; set; }
    public required string GraphBucketId { get; set; }
    public required string Name { get; set; }
    public int OrderHint { get; set; }
}

public class PlannerTaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public PlannerPlan? Plan { get; set; }
    public required string GraphTaskId { get; set; }
    public string? GraphBucketId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public int PercentComplete { get; set; }
    public string? BucketName { get; set; }
    public string Status { get; set; } = "NotStarted";
    /// <summary>Comma-separated display names or AAD object ids.</summary>
    public string? AssignedUsers { get; set; }
    public bool IsDelayed { get; set; }
    public DateTimeOffset LastSyncAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? GraphCreatedAt { get; set; }
    public DateTimeOffset? GraphModifiedAt { get; set; }
}

/// <summary>Daily aggregate snapshot for burndown / trend charts (Phase 5).</summary>
public class PlannerDailySnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly SnapshotDate { get; set; }
    public int TotalTasks { get; set; }
    public int Completed { get; set; }
    public int Incomplete { get; set; }
    public int Delayed { get; set; }
    public int HealthScore { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PlannerSyncResult
{
    public int PlansUpserted { get; set; }
    public int BucketsUpserted { get; set; }
    public int TasksUpserted { get; set; }
    public bool UsedDemoSeed { get; set; }
    public string StatusMessage { get; set; } = "";
    public List<string> Errors { get; set; } = [];
}

public class PlannerHealthSummary
{
    public int TotalTasks { get; set; }
    public int Completed { get; set; }
    public int InProgress { get; set; }
    public int NotStarted { get; set; }
    public int Delayed { get; set; }
    public double CompletionPercent { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string StatusMessage { get; set; } = "";
    public DateTimeOffset? LastSyncAt { get; set; }
    public int PlanCount { get; set; }
}

public class PlannerTaskListItem
{
    public Guid Id { get; set; }
    public required string GraphTaskId { get; set; }
    public required string Title { get; set; }
    public string? PlanTitle { get; set; }
    public string? BucketName { get; set; }
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public int PercentComplete { get; set; }
    public string Status { get; set; } = "NotStarted";
    public string? AssignedUsers { get; set; }
    public bool IsDelayed { get; set; }
}

public class PlannerPlanListItem
{
    public Guid Id { get; set; }
    public required string GraphPlanId { get; set; }
    public required string Title { get; set; }
    public int TaskCount { get; set; }
    public DateTimeOffset LastSyncAt { get; set; }
}

public class PlannerWorkloadRow
{
    public required string Assignee { get; set; }
    public int TotalTasks { get; set; }
    public int Completed { get; set; }
    public int InProgress { get; set; }
    public int Delayed { get; set; }
    public double AvgPercentComplete { get; set; }
}

/// <summary>Daily aggregate snapshot for burndown / trend charts (Phase 5).</summary>
public class PlannerTaskSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly SnapshotDate { get; set; }
    public int TotalTasks { get; set; }
    public int Completed { get; set; }
    public int InProgress { get; set; }
    public int NotStarted { get; set; }
    public int Delayed { get; set; }
    public double CompletionPercent { get; set; }
    public int HealthScore { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class PlannerBurndownPoint
{
    public DateOnly Date { get; set; }
    public int TotalTasks { get; set; }
    public int Completed { get; set; }
    public int InProgress { get; set; }
    public int NotStarted { get; set; }
    public int Delayed { get; set; }
    public double CompletionPercent { get; set; }
    public int HealthScore { get; set; }
}

public class UnifiedIntelligenceRequest
{
    public string? Query { get; set; }
    public required string UserObjectId { get; set; }
    public bool IncludeDelayedTasks { get; set; } = true;
    public bool IncludeSharePoint { get; set; } = true;
    public bool UseLlmSummary { get; set; } = true;
}

public class UnifiedIntelligenceResponse
{
    public string? Query { get; set; }
    public int HealthScore { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string ManagementSummary { get; set; } = "";
    public List<PlannerTaskListItem> DelayedTasks { get; set; } = [];
    public List<UnifiedRelatedDocument> RelatedDocuments { get; set; } = [];
    public List<StaffingRecommendation> StaffingRecommendations { get; set; } = [];
    public bool UsedLlmSummary { get; set; }
    public string? AiProvider { get; set; }
    public string? Model { get; set; }
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class UnifiedRelatedDocument
{
    public required string FileName { get; set; }
    public string? Locator { get; set; }
    public string? Excerpt { get; set; }
    public double Score { get; set; }
    public string? MatchedTaskTitle { get; set; }
}
