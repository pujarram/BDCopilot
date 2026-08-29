namespace BDCopilot.Core.Models;

/// <summary>A single grounded source reference attached to an AI answer.</summary>
public class Citation
{
    public Guid DocumentId { get; set; }

    public required string FileName { get; set; }

    /// <summary>e.g. "Page 12", "Slide 18", "Section 5".</summary>
    public string? Locator { get; set; }

    public required string SharePointUrl { get; set; }

    /// <summary>Short excerpt used to justify the citation, shown on hover in the UI.</summary>
    public string? Snippet { get; set; }
}
