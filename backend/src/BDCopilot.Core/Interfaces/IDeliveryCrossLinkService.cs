using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface IDeliveryCrossLinkService
{
    Task<DeliveryCrossLinkResponse> GetCrossLinksAsync(
        string userObjectId,
        int maxTasks = 8,
        bool refresh = false,
        CancellationToken ct = default);

    Task<CrossLinkFeedbackResult> SubmitFeedbackAsync(
        CrossLinkFeedbackRequest request,
        CancellationToken ct = default);
}
