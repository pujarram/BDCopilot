using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface IOpportunityService
{
    Task<List<Opportunity>> ListAsync(CancellationToken ct = default);
    Task<Opportunity?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Opportunity> CreateAsync(CreateOpportunityRequest request, CancellationToken ct = default);
    Task<Opportunity?> UpdateAsync(Guid id, UpdateOpportunityRequest request, CancellationToken ct = default);
    Task<OpportunityDocumentLink> LinkDocumentAsync(LinkOpportunityDocumentRequest request, CancellationToken ct = default);
    Task TagWinLossAsync(TagWinLossRequest request, CancellationToken ct = default);
}

public interface IMultiApprovalService
{
    Task<MultiApprovalStatus> StartAsync(StartMultiApprovalRequest request, CancellationToken ct = default);
    Task<MultiApprovalStatus> DecideAsync(ReviewerDecisionRequest request, CancellationToken ct = default);
    Task<MultiApprovalStatus?> GetStatusAsync(Guid generationId, CancellationToken ct = default);
    Task<List<MultiApprovalStatus>> ListPendingAsync(int take = 10, CancellationToken ct = default);
}

public interface IDynamicsDealService
{
    Task<DynamicsDealContext?> GetDealContextAsync(string? dynamicsOpportunityId, string? clientName, CancellationToken ct = default);
    Task<List<DynamicsDealContext>> ListDemoDealsAsync(CancellationToken ct = default);
}

public interface IRoiAnalyticsService
{
    Task<RoiDashboardSummary> GetRoiAsync(CancellationToken ct = default);
    Task<CustomerUsageSummary> GetCustomerUsageAsync(CancellationToken ct = default);
}
