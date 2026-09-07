namespace BDCopilot.Core.Models;

/// <summary>Phase 8 — compliance checklist before export.</summary>
public class ExportComplianceChecklist
{
    public Guid GenerationId { get; set; }
    public string DocumentTitle { get; set; } = "";
    public List<ComplianceChecklistItem> Items { get; set; } = [];
    public bool AllRequiredComplete { get; set; }
    public string Guidance { get; set; } = "";
}

public class ComplianceChecklistItem
{
    public required string Id { get; set; }
    public required string Label { get; set; }
    public required string Category { get; set; }
    public bool Required { get; set; } = true;
    public bool Checked { get; set; }
    public string? Notes { get; set; }
}

public class SubmitComplianceChecklistRequest
{
    public required Guid GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public required List<ComplianceChecklistItem> Items { get; set; }
}

public class ComplianceChecklistResult
{
    public Guid GenerationId { get; set; }
    public bool ReadyForExport { get; set; }
    public int RequiredChecked { get; set; }
    public int RequiredTotal { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Stored generation version for diffing.</summary>
public class GenerationSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationId { get; set; }
    public int VersionNumber { get; set; }
    public required string DocumentTitle { get; set; }
    public required string SectionsJson { get; set; }
    public required string CreatedByUserObjectId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ChangeSummary { get; set; }
}

public class SaveGenerationSnapshotRequest
{
    public required Guid GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public string? DisplayName { get; set; }
    public required GeneratedDocument Document { get; set; }
}

public class GenerationVersionDiff
{
    public Guid GenerationId { get; set; }
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    public List<SectionDiffItem> Sections { get; set; } = [];
    public string Summary { get; set; } = "";
}

public class SectionDiffItem
{
    public required string Title { get; set; }
    /// <summary>Added | Removed | Modified | Unchanged</summary>
    public required string ChangeKind { get; set; }
    public string? BeforeExcerpt { get; set; }
    public string? AfterExcerpt { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
}

/// <summary>SOC2 / ISO27001-aligned governance audit event.</summary>
public class GovernanceAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public string? UserDisplayName { get; set; }
    /// <summary>Generate | Approve | Reject | Export | ComplianceCheck | VersionSave | Access</summary>
    public required string EventType { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? Outcome { get; set; }
    public string? Detail { get; set; }
    public string ComplianceFramework { get; set; } = "SOC2-ISO27001";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class GovernanceAuditQuery
{
    public Guid? GenerationId { get; set; }
    public string? EventType { get; set; }
    public int Limit { get; set; } = 50;
}

/// <summary>Phase 9 — AI PM health score explanation.</summary>
public class HealthScoreExplanation
{
    public int HealthScore { get; set; }
    public string RiskLevel { get; set; } = "";
    public List<ScoreFactor> Factors { get; set; } = [];
    public string Narrative { get; set; } = "";
}

public class ScoreFactor
{
    public required string Factor { get; set; }
    public int ImpactPoints { get; set; }
    public string Direction { get; set; } = "negative";
    public string Detail { get; set; } = "";
}

/// <summary>Phase 9 — capacity vs pursuit pipeline staffing gaps.</summary>
public class PipelineCapacityView
{
    public List<PipelineStaffingGap> Gaps { get; set; } = [];
    public List<PipelineOpportunityRow> OpenPursuits { get; set; } = [];
    public string Summary { get; set; } = "";
    public string Guidance { get; set; } = "";
}

public class PipelineStaffingGap
{
    public required string Assignee { get; set; }
    public required string OpportunityName { get; set; }
    public required string Client { get; set; }
    public string Stage { get; set; } = "";
    public DateTimeOffset? Deadline { get; set; }
    public double CurrentFteLoad { get; set; }
    public string GapLevel { get; set; } = "Medium";
    public string Rationale { get; set; } = "";
}

public class PipelineOpportunityRow
{
    public Guid OpportunityId { get; set; }
    public required string Name { get; set; }
    public required string Client { get; set; }
    public string Stage { get; set; } = "";
    public string? OwnerDisplayName { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public int DaysToDeadline { get; set; }
    public bool HasStaffingGap { get; set; }
}

public class PursuitDeadlineAlert
{
    public Guid OpportunityId { get; set; }
    public required string Name { get; set; }
    public required string Client { get; set; }
    public DateTimeOffset Deadline { get; set; }
    public int DaysRemaining { get; set; }
    public string Stage { get; set; } = "";
    public string? OwnerDisplayName { get; set; }
}

public class StaleDocumentAlert
{
    public Guid DocumentId { get; set; }
    public required string FileName { get; set; }
    public int MonthsSinceModified { get; set; }
    public string CorpusSource { get; set; } = "";
}

/// <summary>EF entity — persisted export compliance checklist.</summary>
public class ExportComplianceChecklistRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationId { get; set; }
    public required string ItemsJson { get; set; }
    public required string SubmittedByUserObjectId { get; set; }
    public string? SubmittedByDisplayName { get; set; }
    public bool ReadyForExport { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>EF entity — generation version snapshot.</summary>
public class GenerationSnapshotRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationId { get; set; }
    public int VersionNumber { get; set; }
    public required string DocumentTitle { get; set; }
    public required string SectionsJson { get; set; }
    public required string CreatedByUserObjectId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ChangeSummary { get; set; }
}

/// <summary>EF entity — SOC2 / ISO27001-aligned audit trail.</summary>
public class GovernanceAuditRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GenerationId { get; set; }
    public required string UserObjectId { get; set; }
    public string? UserDisplayName { get; set; }
    public required string EventType { get; set; }
    public required string ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? Outcome { get; set; }
    public string? Detail { get; set; }
    public string ComplianceFramework { get; set; } = "SOC2-ISO27001";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
