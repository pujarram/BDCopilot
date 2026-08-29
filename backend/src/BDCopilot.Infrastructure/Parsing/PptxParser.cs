using BDCopilot.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace BDCopilot.Infrastructure.Parsing;

public class PptxParser
{
    public DocumentParseResult Parse(Stream content)
    {
        var sections = new List<(string Text, string? Locator)>();

        using var presentation = PresentationDocument.Open(content, false);
        var slideParts = presentation.PresentationPart?.SlideParts;
        if (slideParts is null)
        {
            return new DocumentParseResult();
        }

        var slideNumber = 0;
        foreach (var slidePart in slideParts)
        {
            slideNumber++;
            var texts = slidePart.Slide?
                .Descendants<A.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList() ?? [];

            if (texts.Count == 0)
            {
                continue;
            }

            sections.Add((string.Join("\n", texts), $"Slide {slideNumber}"));
        }

        return new DocumentParseResult { Sections = sections };
    }
}
