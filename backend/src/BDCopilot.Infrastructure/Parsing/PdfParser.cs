using BDCopilot.Core.Models;
using UglyToad.PdfPig;

namespace BDCopilot.Infrastructure.Parsing;

public class PdfParser
{
    public DocumentParseResult Parse(Stream content)
    {
        var sections = new List<(string Text, string? Locator)>();

        using var document = PdfDocument.Open(content);
        foreach (var page in document.GetPages())
        {
            var text = page.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            sections.Add((text, $"Page {page.Number}"));
        }

        return new DocumentParseResult { Sections = sections };
    }
}
