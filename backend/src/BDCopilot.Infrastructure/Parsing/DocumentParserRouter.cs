using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Parsing;

public class DocumentParserRouter : IDocumentParserRouter
{
    private readonly DocxParser _docx = new();
    private readonly PptxParser _pptx = new();
    private readonly XlsxParser _xlsx = new();
    private readonly PdfParser _pdf = new();
    private readonly OcrFallbackParser _ocrFallback = new();
    private readonly ILogger<DocumentParserRouter> _logger;

    public DocumentParserRouter(ILogger<DocumentParserRouter> logger)
    {
        _logger = logger;
    }

    public Task<DocumentParseResult> ParseAsync(string fileName, Stream content, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var extension = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        DocumentParseResult result;

        try
        {
            if (content.CanSeek)
            {
                content.Position = 0;
            }

            result = extension switch
            {
                "docx" => _docx.Parse(content),
                "pptx" => _pptx.Parse(content),
                "xlsx" => _xlsx.Parse(content),
                "pdf" => _pdf.Parse(content),
                "txt" or "md" or "markdown" => ParsePlainText(fileName, content),
                _ => new DocumentParseResult
                {
                    IsMetadataOnly = true,
                    Note = $"Unsupported file type '{extension}' for {fileName}.",
                    Sections = [( $"[Unsupported] {fileName} — parser not available for .{extension}.", "Metadata")]
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Parser failed for {FileName}", fileName);
            result = new DocumentParseResult
            {
                IsMetadataOnly = true,
                Note = $"Parse error: {ex.Message}",
                Sections = [( $"[ParseError] {fileName}: {ex.Message}", "Metadata")]
            };
        }

        result = _ocrFallback.Apply(fileName, result);
        return Task.FromResult(result);
    }

    private static DocumentParseResult ParsePlainText(string fileName, Stream content)
    {
        using var reader = new StreamReader(content, leaveOpen: true);
        var text = reader.ReadToEnd();
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DocumentParseResult
            {
                IsMetadataOnly = true,
                Note = "Empty text file.",
                Sections = []
            };
        }

        return new DocumentParseResult
        {
            Sections = [(text.Trim(), Path.GetFileName(fileName))]
        };
    }
}
