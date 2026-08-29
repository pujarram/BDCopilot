using BDCopilot.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BDCopilot.Infrastructure.Parsing;

public class DocxParser
{
    public DocumentParseResult Parse(Stream content)
    {
        var sections = new List<(string Text, string? Locator)>();

        using var doc = WordprocessingDocument.Open(content, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return new DocumentParseResult();
        }

        var currentHeading = "Section 1";
        var paragraphBuffer = new List<string>();

        foreach (var element in body.Elements())
        {
            if (element is Paragraph paragraph)
            {
                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (style.StartsWith("Heading", StringComparison.OrdinalIgnoreCase))
                {
                    FlushParagraphBuffer(sections, currentHeading, paragraphBuffer);
                    currentHeading = text;
                }
                else
                {
                    paragraphBuffer.Add(text);
                }
            }
        }

        FlushParagraphBuffer(sections, currentHeading, paragraphBuffer);
        return new DocumentParseResult { Sections = sections };
    }

    private static void FlushParagraphBuffer(
        List<(string Text, string? Locator)> sections,
        string heading,
        List<string> buffer)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        sections.Add((string.Join("\n", buffer), heading));
        buffer.Clear();
    }
}
