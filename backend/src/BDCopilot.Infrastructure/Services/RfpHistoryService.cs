using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;

namespace BDCopilot.Infrastructure.Services;

public interface IRfpHistoryService
{
    Task<RfpDocument> SaveAsync(SaveRfpDocumentRequest request, CancellationToken ct = default);
    Task<List<RfpDocumentListItem>> ListAsync(string? userObjectId, CancellationToken ct = default);
    Task<RfpDocument?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ExportResult> ExportAsync(Guid id, string userObjectId, string format, CancellationToken ct = default);
    Task<RfpDocument> SaveToBdChannelAsync(SaveRfpToChannelRequest request, CancellationToken ct = default);
}

public class RfpHistoryService : IRfpHistoryService
{
    private readonly BdCopilotDbContext _db;
    private readonly IGenerationWorkflowService _workflow;
    private readonly GraphClientFactory _graphFactory;
    private readonly GraphSyncSettings _graphSettings;
    private readonly ILogger<RfpHistoryService> _logger;

    public RfpHistoryService(
        BdCopilotDbContext db,
        IGenerationWorkflowService workflow,
        GraphClientFactory graphFactory,
        IOptions<GraphSyncSettings> graphSettings,
        ILogger<RfpHistoryService> logger)
    {
        _db = db;
        _workflow = workflow;
        _graphFactory = graphFactory;
        _graphSettings = graphSettings.Value;
        _logger = logger;
    }

    public async Task<RfpDocument> SaveAsync(SaveRfpDocumentRequest request, CancellationToken ct = default)
    {
        var doc = request.Document;
        var existing = await _db.RfpDocuments
            .FirstOrDefaultAsync(r => r.GenerationId == doc.GenerationId, ct);

        var sectionMap = doc.Sections.ToDictionary(
            s => NormalizeTitle(s.Title),
            s => s.Content,
            StringComparer.OrdinalIgnoreCase);

        string? Pick(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (sectionMap.TryGetValue(NormalizeTitle(key), out var value))
                {
                    return value;
                }
            }

            return null;
        }

        if (existing is null)
        {
            existing = new RfpDocument
            {
                GenerationId = doc.GenerationId,
                Title = doc.Title,
                Customer = request.Customer ?? "",
                Tone = request.Tone ?? "Formal",
                Status = doc.Status,
                CreatedByUserObjectId = request.UserObjectId,
                CreatedByDisplayName = request.DisplayName,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.RfpDocuments.Add(existing);
        }
        else
        {
            existing.Title = doc.Title;
            existing.Customer = request.Customer ?? existing.Customer;
            existing.Tone = request.Tone ?? existing.Tone;
            existing.Status = doc.Status;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        existing.ExecutiveSummary = Pick("Executive Summary") ?? existing.ExecutiveSummary;
        existing.UnderstandingOfRequirements = Pick("Understanding of Requirements") ?? existing.UnderstandingOfRequirements;
        existing.ProposedSolutionArchitecture = Pick("Proposed Solution & Architecture", "Proposed Solution and Architecture")
            ?? existing.ProposedSolutionArchitecture;
        existing.SecurityCompliance = Pick("Security & Compliance", "Security and Compliance") ?? existing.SecurityCompliance;
        existing.DeliveryTimelineTeam = Pick("Delivery Timeline & Team", "Delivery Timeline and Team")
            ?? existing.DeliveryTimelineTeam;
        existing.CommercialsPricing = Pick("Commercials & Pricing", "Commercials and Pricing")
            ?? existing.CommercialsPricing;

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<List<RfpDocumentListItem>> ListAsync(string? userObjectId, CancellationToken ct = default)
    {
        var query = _db.RfpDocuments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userObjectId))
        {
            query = query.Where(r => r.CreatedByUserObjectId == userObjectId);
        }

        return await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .Select(r => new RfpDocumentListItem
            {
                Id = r.Id,
                GenerationId = r.GenerationId,
                Title = r.Title,
                Customer = r.Customer,
                Status = r.Status,
                CreatedByUserObjectId = r.CreatedByUserObjectId,
                CreatedByDisplayName = r.CreatedByDisplayName,
                CreatedAt = r.CreatedAt,
                ChannelUploadStatus = r.ChannelUploadStatus,
                ChannelSharePointUrl = r.ChannelSharePointUrl,
                Outcome = r.Outcome,
                OpportunityId = r.OpportunityId
            })
            .ToListAsync(ct);
    }

    public Task<RfpDocument?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.RfpDocuments.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ExportResult> ExportAsync(Guid id, string userObjectId, string format, CancellationToken ct = default)
    {
        var row = await _db.RfpDocuments.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException($"RFP document {id} was not found.");

        var generated = ToGeneratedDocument(row);
        var result = await _workflow.ExportAsync(new ExportGenerationRequest
            {
                GenerationId = row.GenerationId,
                UserObjectId = userObjectId,
                Format = format,
                Document = generated,
                RequireApproved = false
            }, ct);

        row.Status = "Exported";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<RfpDocument> SaveToBdChannelAsync(SaveRfpToChannelRequest request, CancellationToken ct = default)
    {
        var row = await _db.RfpDocuments.FirstOrDefaultAsync(r => r.Id == request.RfpDocumentId, ct)
            ?? throw new InvalidOperationException($"RFP document {request.RfpDocumentId} was not found.");

        var graph = _graphFactory.GetClient();
        if (graph is null || string.IsNullOrWhiteSpace(_graphSettings.BdChannelDriveId))
        {
            row.ChannelUploadStatus = "SkippedNoGraph";
            row.ChannelUploadError =
                "Graph / BdChannelDriveId is not configured yet. Document is saved in the database; " +
                "channel upload will work after Entra + Graph + drive id are provisioned.";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return row;
        }

        // Graph is configured — mark ready for channel upload. Drive ItemWithPath differs by
        // Graph SDK version; keep DB + Export working now and complete PUT once Admin provides drive id.
        row.ChannelUploadStatus = "PendingGraphUpload";
        row.ChannelUploadError =
            $"Queued for BD channel drive '{_graphSettings.BdChannelDriveId}' folder '{_graphSettings.RfpFolderPath}'. " +
            "Document is in PostgreSQL; use Export until live Graph upload is finalized.";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return row;
    }

    private static GeneratedDocument ToGeneratedDocument(RfpDocument row) =>
        new()
        {
            GenerationId = row.GenerationId,
            Title = row.Title,
            Status = "Approved",
            CreatedAt = row.CreatedAt,
            Sections =
            [
                Section(1, "Executive Summary", row.ExecutiveSummary),
                Section(2, "Understanding of Requirements", row.UnderstandingOfRequirements),
                Section(3, "Proposed Solution & Architecture", row.ProposedSolutionArchitecture),
                Section(4, "Security & Compliance", row.SecurityCompliance),
                Section(5, "Delivery Timeline & Team", row.DeliveryTimelineTeam),
                Section(6, "Commercials & Pricing", row.CommercialsPricing)
            ]
        };

    private static GeneratedSection Section(int order, string title, string? content) =>
        new()
        {
            Order = order,
            Title = title,
            Content = content ?? "",
            Sources = []
        };

    private static string NormalizeTitle(string title) =>
        title.Trim().ToLowerInvariant()
            .Replace("&", "and", StringComparison.Ordinal)
            .Replace("  ", " ", StringComparison.Ordinal);

    private static string Sanitize(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "rfp" : cleaned;
    }
}
