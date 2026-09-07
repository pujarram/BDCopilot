namespace BDCopilot.Core.Models;

/// <summary>Phase 6 — opportunity / pursuit tracker.</summary>
public class Opportunity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Client { get; set; }
    public decimal? DealSize { get; set; }
    /// <summary>Lead | Qualify | Propose | Negotiate | ClosedWon | ClosedLost</summary>
    public string Stage { get; set; } = "Lead";
    public string? OwnerDisplayName { get; set; }
    public string? OwnerUserObjectId { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    /// <summary>Open | Won | Lost</summary>
    public string Outcome { get; set; } = "Open";
    public string? OutcomeNotes { get; set; }
    public string? DynamicsOpportunityId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<OpportunityDocumentLink> LinkedDocuments { get; set; } = [];
}

public class OpportunityDocumentLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OpportunityId { get; set; }
    public Guid? GenerationId { get; set; }
    public Guid? RfpDocumentId { get; set; }
    public Guid? HistoryDocumentId { get; set; }
    /// <summary>Rfp | BusinessCase | Proposal | Competitive | Other</summary>
    public string DocumentType { get; set; } = "Rfp";
    public string? Title { get; set; }
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class CreateOpportunityRequest
{
    public required string Name { get; set; }
    public required string Client { get; set; }
    public decimal? DealSize { get; set; }
    public string Stage { get; set; } = "Lead";
    public string? OwnerDisplayName { get; set; }
    public string? OwnerUserObjectId { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? Notes { get; set; }
    public string? DynamicsOpportunityId { get; set; }
}

public class UpdateOpportunityRequest
{
    public string? Name { get; set; }
    public string? Client { get; set; }
    public decimal? DealSize { get; set; }
    public string? Stage { get; set; }
    public string? OwnerDisplayName { get; set; }
    public string? OwnerUserObjectId { get; set; }
    public DateTimeOffset? Deadline { get; set; }
    public string? Notes { get; set; }
    public string? Outcome { get; set; }
    public string? OutcomeNotes { get; set; }
}

public class LinkOpportunityDocumentRequest
{
    public Guid OpportunityId { get; set; }
    public Guid? GenerationId { get; set; }
    public Guid? RfpDocumentId { get; set; }
    public Guid? HistoryDocumentId { get; set; }
    public string DocumentType { get; set; } = "Rfp";
    public string? Title { get; set; }
}

public class TagWinLossRequest
{
    public Guid RfpDocumentId { get; set; }
    /// <summary>Win | Loss | Open</summary>
    public required string Outcome { get; set; }
    public string? Notes { get; set; }
    public Guid? OpportunityId { get; set; }
    public required string UserObjectId { get; set; }
}

/// <summary>Boosts retrieval score for chunks from documents linked to won RFPs.</summary>
public class DocumentWinBoost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid? SourceRfpDocumentId { get; set; }
    public double Boost { get; set; } = 0.08;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Phase 6 — multi-reviewer approval (Legal + Sales).</summary>
public class GenerationApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GenerationId { get; set; }
    public string DocumentTitle { get; set; } = "";
    /// <summary>Legal | Sales</summary>
    public required string Role { get; set; }
    /// <summary>Pending | Approved | Rejected</summary>
    public string Status { get; set; } = "Pending";
    public string? ReviewerUserObjectId { get; set; }
    public string? ReviewerDisplayName { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }
}

public class StartMultiApprovalRequest
{
    public required Guid GenerationId { get; set; }
    public required string DocumentTitle { get; set; }
    public required string UserObjectId { get; set; }
    public List<string> RequiredRoles { get; set; } = ["Legal", "Sales"];
}

public class ReviewerDecisionRequest
{
    public required Guid GenerationId { get; set; }
    public required string Role { get; set; }
    /// <summary>Approved | Rejected</summary>
    public required string Status { get; set; }
    public required string UserObjectId { get; set; }
    public string? DisplayName { get; set; }
    public string? Notes { get; set; }
}

public class MultiApprovalStatus
{
    public Guid GenerationId { get; set; }
    public string DocumentTitle { get; set; } = "";
    public List<GenerationApproval> Reviews { get; set; } = [];
    public bool IsFullyApproved { get; set; }
    public bool IsRejected { get; set; }
    public string OverallStatus { get; set; } = "Pending";
}

public class CompetitivePositioningRequest
{
    public required string Competitor { get; set; }
    public required string OurSolution { get; set; }
    public string? CustomerContext { get; set; }
    public required string UserObjectId { get; set; }
    public string? Language { get; set; }
    public string? ComplianceRegion { get; set; }
    public Guid? OpportunityId { get; set; }
}

/// <summary>Phase 7 — Dynamics deal context for generators.</summary>
public class DynamicsSettings
{
    public const string SectionName = "Dynamics";

    public bool Enabled { get; set; }
    public string? DataverseUrl { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? TenantId { get; set; }
    /// <summary>When true (default for demos), return seeded deal context without calling Dataverse.</summary>
    public bool UseDemoSeed { get; set; } = true;
}

public class DynamicsDealContext
{
    public string OpportunityId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Client { get; set; } = "";
    public decimal? EstimatedValue { get; set; }
    public string Stage { get; set; } = "";
    public string? Owner { get; set; }
    public DateTimeOffset? CloseDate { get; set; }
    public string FocusNotes { get; set; } = "";
    public bool FromDemoSeed { get; set; }
}

public class LibraryStalenessSettings
{
    public const string SectionName = "LibraryStaleness";
    public int StaleAfterMonths { get; set; } = 6;
}

public class RoiDashboardSummary
{
    public double MinutesSavedPerDocument { get; set; } = 45;
    public double HoursSavedTotal { get; set; }
    public int GenerationsThisMonth { get; set; }
    public int GenerationsLast30Days { get; set; }
    public List<MonthlyGenerationPoint> GenerationsByMonth { get; set; } = [];
    public double WinRatePercent { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int OpenOutcomes { get; set; }
    public List<WinRateTrendPoint> WinRateTrend { get; set; } = [];
    public long TokensLast30Days { get; set; }
    public int RequestsLast30Days { get; set; }
    public string Guidance { get; set; } = "";
}

public class MonthlyGenerationPoint
{
    public string Month { get; set; } = "";
    public int Count { get; set; }
}

public class WinRateTrendPoint
{
    public string Month { get; set; } = "";
    public int Wins { get; set; }
    public int Losses { get; set; }
    public double WinRatePercent { get; set; }
}

public class CustomerUsageSummary
{
    public long TokensLast30Days { get; set; }
    public int RequestsLast30Days { get; set; }
    public int GenerationsLast30Days { get; set; }
    public double EstimatedCostUsd { get; set; }
    public double CostPer1kTokensUsd { get; set; } = 0.002;
    public List<TeamTokenCostRow> TopConsumers { get; set; } = [];
    public List<OperationUsageRow> ByOperation { get; set; } = [];
    public string Guidance { get; set; } = "";
}

public class OperationUsageRow
{
    public string Operation { get; set; } = "";
    public int RequestCount { get; set; }
    public long TotalTokens { get; set; }
}
