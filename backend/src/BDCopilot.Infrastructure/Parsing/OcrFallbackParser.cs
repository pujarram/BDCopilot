using BDCopilot.Core.Models;

namespace BDCopilot.Infrastructure.Parsing;

/// <summary>
/// When text extraction yields almost nothing (likely scanned PDF/image), returns a MetadataOnly
/// placeholder instead of empty chunks. Full Azure AI Document Intelligence OCR is Phase 4+.
/// </summary>
public class OcrFallbackParser
{
    private const int MinimumMeaningfulCharacters = 40;

    public DocumentParseResult Apply(string fileName, DocumentParseResult inner)
    {
        var totalChars = inner.Sections.Sum(s => s.Text.Length);
        if (totalChars >= MinimumMeaningfulCharacters)
        {
            return inner;
        }

        return new DocumentParseResult
        {
            IsMetadataOnly = true,
            Note =
                $"Text extraction for '{fileName}' yielded insufficient content ({totalChars} chars). " +
                "Document indexed as MetadataOnly — OCR / Document Intelligence not yet configured.",
            Sections =
            [
                ($"[MetadataOnly] {fileName} — content not machine-readable; manual review required.", "Metadata")
            ]
        };
    }
}
