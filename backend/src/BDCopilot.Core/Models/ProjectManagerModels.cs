namespace BDCopilot.Core.Models;

/// <summary>Phase 4 — AI Project Manager insights over Planner snapshots.</summary>
public class ProjectManagerInsight
{
    public int HealthScore { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string Summary { get; set; } = "";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<DelayPredictionItem> DelayPredictions { get; set; } = [];
    public List<ModuleAtRiskItem> ModulesAtRisk { get; set; } = [];
    public List<StaffingRecommendation> StaffingRecommendations { get; set; } = [];
}

public class DelayPredictionItem
{
    public Guid TaskId { get; set; }
    public required string Title { get; set; }
    public string? Assignee { get; set; }
    public string? BucketName { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public int PercentComplete { get; set; }
    public bool IsAlreadyDelayed { get; set; }
    /// <summary>Estimated days of slip (positive = behind).</summary>
    public int PredictedSlipDays { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string Rationale { get; set; } = "";
}

public class ModuleAtRiskItem
{
    public required string ModuleName { get; set; }
    public int TaskCount { get; set; }
    public int DelayedCount { get; set; }
    public int IncompleteCount { get; set; }
    public double AvgPercentComplete { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public string Recommendation { get; set; } = "";
}

public class StaffingRecommendation
{
    public required string Focus { get; set; }
    public string Priority { get; set; } = "Medium";
    public string Detail { get; set; } = "";
}

public class StakeholderWeeklyReport
{
    public required string Title { get; set; }
    public required string MarkdownBody { get; set; }
    public int HealthScore { get; set; }
    public string RiskLevel { get; set; } = "Low";
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool UsedLlmNarrative { get; set; }
    public string? AiProvider { get; set; }
    public string? Model { get; set; }
}
