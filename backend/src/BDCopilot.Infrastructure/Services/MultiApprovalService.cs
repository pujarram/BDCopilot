using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

public sealed class MultiApprovalService : IMultiApprovalService
{
    private readonly BdCopilotDbContext _db;

    public MultiApprovalService(BdCopilotDbContext db) => _db = db;

    public async Task<MultiApprovalStatus> StartAsync(StartMultiApprovalRequest request, CancellationToken ct = default)
    {
        var roles = request.RequiredRoles.Count > 0
            ? request.RequiredRoles.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : ["Legal", "Sales"];

        var existing = await _db.GenerationApprovals
            .Where(a => a.GenerationId == request.GenerationId)
            .ToListAsync(ct);
        if (existing.Count > 0)
        {
            _db.GenerationApprovals.RemoveRange(existing);
        }

        foreach (var role in roles)
        {
            _db.GenerationApprovals.Add(new GenerationApproval
            {
                GenerationId = request.GenerationId,
                DocumentTitle = request.DocumentTitle,
                Role = role,
                Status = "Pending"
            });
        }

        await _db.SaveChangesAsync(ct);
        return (await GetStatusAsync(request.GenerationId, ct))!;
    }

    public async Task<MultiApprovalStatus> DecideAsync(ReviewerDecisionRequest request, CancellationToken ct = default)
    {
        var row = await _db.GenerationApprovals
            .FirstOrDefaultAsync(
                a => a.GenerationId == request.GenerationId
                     && a.Role.ToLower() == request.Role.Trim().ToLower(),
                ct)
            ?? throw new InvalidOperationException(
                $"No {request.Role} review slot for generation {request.GenerationId}. Start multi-approval first.");

        var status = request.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase) ? "Approved"
            : request.Status.Equals("Rejected", StringComparison.OrdinalIgnoreCase) ? "Rejected"
            : throw new ArgumentException("Status must be Approved or Rejected.");

        row.Status = status;
        row.ReviewerUserObjectId = request.UserObjectId;
        row.ReviewerDisplayName = request.DisplayName;
        row.Notes = request.Notes;
        row.DecidedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return (await GetStatusAsync(request.GenerationId, ct))!;
    }

    public async Task<MultiApprovalStatus?> GetStatusAsync(Guid generationId, CancellationToken ct = default)
    {
        var reviews = await _db.GenerationApprovals
            .Where(a => a.GenerationId == generationId)
            .OrderBy(a => a.Role)
            .ToListAsync(ct);
        if (reviews.Count == 0) return null;

        var rejected = reviews.Any(r => r.Status == "Rejected");
        var fullyApproved = reviews.All(r => r.Status == "Approved");
        return new MultiApprovalStatus
        {
            GenerationId = generationId,
            DocumentTitle = reviews[0].DocumentTitle,
            Reviews = reviews,
            IsFullyApproved = fullyApproved,
            IsRejected = rejected,
            OverallStatus = rejected ? "Rejected" : fullyApproved ? "Approved" : "Pending"
        };
    }

    public async Task<List<MultiApprovalStatus>> ListPendingAsync(int take = 10, CancellationToken ct = default)
    {
        var pendingIds = await _db.GenerationApprovals
            .Where(a => a.Status == "Pending")
            .Select(a => a.GenerationId)
            .Distinct()
            .Take(Math.Clamp(take, 1, 25))
            .ToListAsync(ct);

        var results = new List<MultiApprovalStatus>();
        foreach (var id in pendingIds)
        {
            var status = await GetStatusAsync(id, ct);
            if (status is not null && !status.IsFullyApproved && !status.IsRejected)
            {
                results.Add(status);
            }
        }

        return results;
    }
}
