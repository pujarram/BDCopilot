using BDCopilot.Core.Models;
using ClosedXML.Excel;

namespace BDCopilot.Infrastructure.Parsing;

public class XlsxParser
{
    public DocumentParseResult Parse(Stream content)
    {
        var sections = new List<(string Text, string? Locator)>();

        using var workbook = new XLWorkbook(content);
        foreach (var worksheet in workbook.Worksheets)
        {
            var rows = new List<string>();
            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                continue;
            }

            foreach (var row in usedRange.RowsUsed())
            {
                var cells = row.CellsUsed()
                    .Select(c => c.GetString().Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v));
                var line = string.Join(" | ", cells);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    rows.Add(line);
                }
            }

            if (rows.Count > 0)
            {
                sections.Add((string.Join("\n", rows), worksheet.Name));
            }
        }

        return new DocumentParseResult { Sections = sections };
    }
}
