using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

public interface IDocumentHistoryService
{
    Task<GeneratedDocumentHistory> SaveAsync(SaveGeneratedDocumentHistoryRequest request, CancellationToken ct = default);
    Task<List<GeneratedDocumentHistoryListItem>> ListAsync(string documentType, string? userObjectId, CancellationToken ct = default);
    Task<GeneratedDocumentHistory?> GetAsync(Guid id, CancellationToken ct = default);
    Task<ExportResult> ExportAsync(Guid id, string userObjectId, string format, CancellationToken ct = default);
    Task<GeneratedDocumentHistory> SaveToBdChannelAsync(SaveGeneratedDocumentToChannelRequest request, CancellationToken ct = default);
}

public class DocumentHistoryService : IDocumentHistoryService
{
    private readonly BdCopilotDbContext _db;
    private readonly IGenerationWorkflowService _workflow;
    private readonly GraphClientFactory _graphFactory;
    private readonly GraphSyncSettings _graphSettings;
    private readonly ILogger<DocumentHistoryService> _logger;

    public DocumentHistoryService(
        BdCopilotDbContext db,
        IGenerationWorkflowService workflow,
        GraphClientFactory graphFactory,
        IOptions<GraphSyncSettings> graphSettings,
        ILogger<DocumentHistoryService> logger)
    {
        _db = db;
        _workflow = workflow;
        _graphFactory = graphFactory;
        _graphSettings = graphSettings.Value;
        _logger = logger;
    }

    public async Task<GeneratedDocumentHistory> SaveAsync(SaveGeneratedDocumentHistoryRequest request, CancellationToken ct = default)
    {
        var docType = NormalizeType(request.DocumentType);
        var doc = request.Document;

        var existing = await _db.GeneratedDocumentHistories
            .FirstOrDefaultAsync(r => r.GenerationId == doc.GenerationId && r.DocumentType == docType, ct);

        if (existing is null)
        {
            existing = new GeneratedDocumentHistory
            {
                GenerationId = doc.GenerationId,
                DocumentType = docType,
                Title = doc.Title,
                Status = doc.Status,
                CreatedByUserObjectId = request.UserObjectId,
                CreatedByDisplayName = request.DisplayName,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _db.GeneratedDocumentHistories.Add(existing);
        }
        else
        {
            existing.Title = doc.Title;
            existing.Status = doc.Status;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        existing.SetSections(doc.Sections);
        if (request.Metadata is not null)
        {
            existing.SetMetadata(request.Metadata);
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<List<GeneratedDocumentHistoryListItem>> ListAsync(
        string documentType, string? userObjectId, CancellationToken ct = default)
    {
        var docType = NormalizeType(documentType);
        var query = _db.GeneratedDocumentHistories.AsNoTracking()
            .Where(r => r.DocumentType == docType);

        if (!string.IsNullOrWhiteSpace(userObjectId))
        {
            query = query.Where(r => r.CreatedByUserObjectId == userObjectId);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        return rows.Select(r =>
        {
            var meta = r.GetMetadata();
            var summary = meta.GetValueOrDefault("initiative")
                          ?? meta.GetValueOrDefault("solution")
                          ?? meta.GetValueOrDefault("audience");
            return new GeneratedDocumentHistoryListItem
            {
                Id = r.Id,
                GenerationId = r.GenerationId,
                DocumentType = r.DocumentType,
                Title = r.Title,
                Status = r.Status,
                CreatedByUserObjectId = r.CreatedByUserObjectId,
                CreatedByDisplayName = r.CreatedByDisplayName,
                CreatedAt = r.CreatedAt,
                ChannelUploadStatus = r.ChannelUploadStatus,
                ChannelSharePointUrl = r.ChannelSharePointUrl,
                SummaryLabel = summary
            };
        }).ToList();
    }

    public Task<GeneratedDocumentHistory?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.GeneratedDocumentHistories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ExportResult> ExportAsync(Guid id, string userObjectId, string format, CancellationToken ct = default)
    {
        var row = await _db.GeneratedDocumentHistories.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException($"History document {id} was not found.");

        var generated = new GeneratedDocument
        {
            GenerationId = row.GenerationId,
            Title = row.Title,
            Status = row.Status,
            CreatedAt = row.CreatedAt,
            Sections = row.GetSections()
        };

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

    public async Task<GeneratedDocumentHistory> SaveToBdChannelAsync(
        SaveGeneratedDocumentToChannelRequest request, CancellationToken ct = default)
    {
        var row = await _db.GeneratedDocumentHistories.FirstOrDefaultAsync(r => r.Id == request.HistoryId, ct)
            ?? throw new InvalidOperationException($"History document {request.HistoryId} was not found.");

        if (!_graphFactory.IsConfigured || string.IsNullOrWhiteSpace(_graphSettings.BdChannelDriveId))
        {
            row.ChannelUploadStatus = "SkippedNoGraph";
            row.ChannelUploadError =
                "Graph credentials or Graph:BdChannelDriveId not configured — draft remains in the database.";
            row.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return row;
        }

        var folder = row.DocumentType switch
        {
            "BusinessCase" => string.IsNullOrWhiteSpace(_graphSettings.BusinessCaseFolderPath)
                ? "Business Cases"
                : _graphSettings.BusinessCaseFolderPath,
            "Proposal" => string.IsNullOrWhiteSpace(_graphSettings.ProposalFolderPath)
                ? "Proposals"
                : _graphSettings.ProposalFolderPath,
            "BattleCard" => "Battlecards",
            _ => _graphSettings.RfpFolderPath
        };

        row.ChannelUploadStatus = "PendingGraphUpload";
        row.ChannelUploadError =
            $"Upload to drive {_graphSettings.BdChannelDriveId}/{folder} is queued for Graph wiring.";
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Channel upload pending for {Type} history {Id} → folder {Folder}",
            row.DocumentType, row.Id, folder);
        return row;
    }

    private static string NormalizeType(string? documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType))
        {
            throw new ArgumentException(
                "Document type is required. Use BusinessCase, Proposal, or BattleCard.");
        }

        return documentType.Trim() switch
        {
            var t when t.Equals("business-case", StringComparison.OrdinalIgnoreCase)
                       || t.Equals("BusinessCase", StringComparison.OrdinalIgnoreCase) => "BusinessCase",
            var t when t.Equals("proposal", StringComparison.OrdinalIgnoreCase)
                       || t.Equals("Proposal", StringComparison.OrdinalIgnoreCase) => "Proposal",
            var t when t.Equals("battle-card", StringComparison.OrdinalIgnoreCase)
                       || t.Equals("battlecard", StringComparison.OrdinalIgnoreCase)
                       || t.Equals("BattleCard", StringComparison.OrdinalIgnoreCase)
                       || t.Equals("competitive", StringComparison.OrdinalIgnoreCase)
                       || t.Equals("Competitive", StringComparison.OrdinalIgnoreCase) => "BattleCard",
            _ => throw new ArgumentException(
                $"Unsupported document type '{documentType}'. Use BusinessCase, Proposal, or BattleCard.")
        };
    }
}
