namespace BDCopilot.Core.Models;

/// <summary>Output of a document parser — one or more text sections with optional locators.</summary>
public class DocumentParseResult
{
    public IReadOnlyList<(string Text, string? Locator)> Sections { get; init; } =
        Array.Empty<(string Text, string? Locator)>();

    public bool IsMetadataOnly { get; init; }

    public string? Note { get; init; }
}
