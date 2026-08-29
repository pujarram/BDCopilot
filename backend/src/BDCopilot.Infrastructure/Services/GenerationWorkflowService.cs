using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public class GenerationWorkflowService : IGenerationWorkflowService
{
    private readonly IDocumentExportService _exportService;
    private readonly IBlobStorageService _blobStorage;
    private readonly BdCopilotDbContext _db;
    private readonly ILogger<GenerationWorkflowService> _logger;

    public GenerationWorkflowService(
        IDocumentExportService exportService,
        IBlobStorageService blobStorage,
        BdCopilotDbContext db,
        ILogger<GenerationWorkflowService> logger)
    {
        _exportService = exportService;
        _blobStorage = blobStorage;
        _db = db;
        _logger = logger;
    }

    public async Task<GeneratedDocument> ApproveAsync(ApproveGenerationRequest request, CancellationToken ct = default)
    {
        request.Document.Status = "Approved";

        await LogFeedbackAsync(new GenerationFeedbackRequest
        {
            GenerationId = request.GenerationId,
            UserObjectId = request.UserObjectId,
            Action = "Approve",
            Notes = $"Approved document '{request.Document.Title}'"
        }, ct);

        _logger.LogInformation(
            "Generation {GenerationId} approved by {UserObjectId}",
            request.GenerationId, request.UserObjectId);

        return request.Document;
    }

    public async Task LogFeedbackAsync(GenerationFeedbackRequest request, CancellationToken ct = default)
    {
        _db.GenerationFeedback.Add(new GenerationFeedback
        {
            GenerationId = request.GenerationId,
            UserObjectId = request.UserObjectId,
            Action = request.Action,
            SectionTitle = request.SectionTitle,
            Notes = request.Notes
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<ExportResult> ExportAsync(ExportGenerationRequest request, CancellationToken ct = default)
    {
        if (request.RequireApproved &&
            !string.Equals(request.Document.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Generation {request.GenerationId} must be Approved before export (current status: {request.Document.Status}).");
        }

        var format = request.Format.Trim().ToLowerInvariant();
        Stream exportStream;
        string fileName;
        string contentType;

        switch (format)
        {
            case "docx":
                exportStream = await _exportService.ExportDocxAsync(request.Document, ct);
                fileName = $"{SanitizeFileName(request.Document.Title)}.docx";
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                break;
            case "pptx":
                exportStream = await _exportService.ExportPptxAsync(request.Document, ct);
                fileName = $"{SanitizeFileName(request.Document.Title)}.pptx";
                contentType = "application/vnd.openxmlformats-officedocument.presentationml.presentation";
                break;
            case "zip":
                exportStream = await _exportService.ExportZipAsync(request.Document, ct);
                fileName = $"{SanitizeFileName(request.Document.Title)}.zip";
                contentType = "application/zip";
                break;
            default:
                throw new ArgumentException($"Unsupported export format '{request.Format}'. Use docx, pptx, or zip.");
        }

        await using (exportStream)
        {
            var downloadUrl = await _blobStorage.UploadGeneratedFileAsync(fileName, exportStream, contentType, ct);

            await LogFeedbackAsync(new GenerationFeedbackRequest
            {
                GenerationId = request.GenerationId,
                UserObjectId = request.UserObjectId,
                Action = "Export",
                Notes = $"Exported as {format}"
            }, ct);

            return new ExportResult
            {
                FileName = fileName,
                ContentType = contentType,
                DownloadUrl = downloadUrl,
                Status = "Ready"
            };
        }
    }

    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "document" : cleaned;
    }
}
