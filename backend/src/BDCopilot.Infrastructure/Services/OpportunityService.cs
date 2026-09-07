using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public sealed class OpportunityService : IOpportunityService
{
    private readonly BdCopilotDbContext _db;
    private readonly ILogger<OpportunityService> _logger;

    public OpportunityService(BdCopilotDbContext db, ILogger<OpportunityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<List<Opportunity>> ListAsync(CancellationToken ct = default) =>
        _db.Opportunities
            .Include(o => o.LinkedDocuments)
            .OrderByDescending(o => o.UpdatedAt ?? o.CreatedAt)
            .ToListAsync(ct);

    public Task<Opportunity?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.Opportunities.Include(o => o.LinkedDocuments).FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Opportunity> CreateAsync(CreateOpportunityRequest request, CancellationToken ct = default)
    {
        var row = new Opportunity
        {
            Name = request.Name.Trim(),
            Client = request.Client.Trim(),
            DealSize = request.DealSize,
            Stage = string.IsNullOrWhiteSpace(request.Stage) ? "Lead" : request.Stage.Trim(),
            OwnerDisplayName = request.OwnerDisplayName,
            OwnerUserObjectId = request.OwnerUserObjectId,
            Deadline = request.Deadline,
            Notes = request.Notes,
            DynamicsOpportunityId = request.DynamicsOpportunityId
        };
        _db.Opportunities.Add(row);
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<Opportunity?> UpdateAsync(Guid id, UpdateOpportunityRequest request, CancellationToken ct = default)
    {
        var row = await _db.Opportunities.Include(o => o.LinkedDocuments).FirstOrDefaultAsync(o => o.Id == id, ct);
        if (row is null) return null;

        if (!string.IsNullOrWhiteSpace(request.Name)) row.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Client)) row.Client = request.Client.Trim();
        if (request.DealSize.HasValue) row.DealSize = request.DealSize;
        if (!string.IsNullOrWhiteSpace(request.Stage)) row.Stage = request.Stage.Trim();
        if (request.OwnerDisplayName is not null) row.OwnerDisplayName = request.OwnerDisplayName;
        if (request.OwnerUserObjectId is not null) row.OwnerUserObjectId = request.OwnerUserObjectId;
        if (request.Deadline.HasValue) row.Deadline = request.Deadline;
        if (request.Notes is not null) row.Notes = request.Notes;
        if (!string.IsNullOrWhiteSpace(request.Outcome))
        {
            row.Outcome = request.Outcome.Trim();
            row.OutcomeNotes = request.OutcomeNotes;
            if (string.Equals(row.Outcome, "Won", StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Outcome, "ClosedWon", StringComparison.OrdinalIgnoreCase))
            {
                row.Stage = "ClosedWon";
            }
            else if (string.Equals(row.Outcome, "Lost", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(row.Outcome, "ClosedLost", StringComparison.OrdinalIgnoreCase))
            {
                row.Stage = "ClosedLost";
            }
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return row;
    }

    public async Task<OpportunityDocumentLink> LinkDocumentAsync(
        LinkOpportunityDocumentRequest request,
        CancellationToken ct = default)
    {
        var link = new OpportunityDocumentLink
        {
            OpportunityId = request.OpportunityId,
            GenerationId = request.GenerationId,
            RfpDocumentId = request.RfpDocumentId,
            HistoryDocumentId = request.HistoryDocumentId,
            DocumentType = request.DocumentType,
            Title = request.Title
        };
        _db.OpportunityDocumentLinks.Add(link);

        var opp = await _db.Opportunities.FirstOrDefaultAsync(o => o.Id == request.OpportunityId, ct);
        if (opp is not null) opp.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.RfpDocumentId.HasValue)
        {
            var rfp = await _db.RfpDocuments.FirstOrDefaultAsync(r => r.Id == request.RfpDocumentId.Value, ct);
            if (rfp is not null) rfp.OpportunityId = request.OpportunityId;
        }

        await _db.SaveChangesAsync(ct);
        return link;
    }

    public async Task TagWinLossAsync(TagWinLossRequest request, CancellationToken ct = default)
    {
        var rfp = await _db.RfpDocuments.FirstOrDefaultAsync(r => r.Id == request.RfpDocumentId, ct)
                  ?? throw new InvalidOperationException("RFP document not found.");

        var outcome = request.Outcome.Trim();
        if (!new[] { "Win", "Loss", "Open" }.Contains(outcome, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Outcome must be Win, Loss, or Open.");
        }

        rfp.Outcome = char.ToUpperInvariant(outcome[0]) + outcome[1..].ToLowerInvariant();
        if (rfp.Outcome is "Win" or "Loss" or "Open") { /* normalized enough */ }
        rfp.Outcome = outcome.Equals("Win", StringComparison.OrdinalIgnoreCase) ? "Win"
            : outcome.Equals("Loss", StringComparison.OrdinalIgnoreCase) ? "Loss" : "Open";
        rfp.OutcomeNotes = request.Notes;
        rfp.OutcomeTaggedAt = DateTimeOffset.UtcNow;
        if (request.OpportunityId.HasValue) rfp.OpportunityId = request.OpportunityId;

        if (request.OpportunityId.HasValue)
        {
            var opp = await _db.Opportunities.FirstOrDefaultAsync(o => o.Id == request.OpportunityId.Value, ct);
            if (opp is not null)
            {
                opp.Outcome = rfp.Outcome == "Win" ? "Won" : rfp.Outcome == "Loss" ? "Lost" : "Open";
                opp.OutcomeNotes = request.Notes;
                opp.Stage = rfp.Outcome == "Win" ? "ClosedWon" : rfp.Outcome == "Loss" ? "ClosedLost" : opp.Stage;
                opp.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        // Win language boost: raise retrieval score for Online docs matching the customer name.
        if (rfp.Outcome == "Win")
        {
            var customer = rfp.Customer.Trim();
            var matches = await _db.Documents
                .Where(d => d.FileName.Contains(customer) || (d.TeamsChannel != null && d.TeamsChannel.Contains(customer)))
                .Select(d => d.DocumentId)
                .Take(40)
                .ToListAsync(ct);

            foreach (var docId in matches)
            {
                var existing = await _db.DocumentWinBoosts
                    .FirstOrDefaultAsync(b => b.DocumentId == docId && b.SourceRfpDocumentId == rfp.Id, ct);
                if (existing is null)
                {
                    _db.DocumentWinBoosts.Add(new DocumentWinBoost
                    {
                        DocumentId = docId,
                        SourceRfpDocumentId = rfp.Id,
                        Boost = 0.08
                    });
                }
            }

            _logger.LogInformation(
                "Win tag on RFP {RfpId} boosted {Count} documents for customer {Customer}",
                rfp.Id, matches.Count, customer);
        }
        else if (rfp.Outcome == "Loss" || rfp.Outcome == "Open")
        {
            var boosts = await _db.DocumentWinBoosts
                .Where(b => b.SourceRfpDocumentId == rfp.Id)
                .ToListAsync(ct);
            _db.DocumentWinBoosts.RemoveRange(boosts);
        }

        await _db.SaveChangesAsync(ct);
    }
}
