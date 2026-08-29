namespace BDCopilot.Core.Models;

public class GeneratedSection
{
    public int Order { get; set; }
    public required string Title { get; set; }
    public required string Content { get; set; }
    public List<Citation> Sources { get; set; } = new();
}

public class GeneratedDocument
{
    public Guid GenerationId { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public List<GeneratedSection> Sections { get; set; } = new();

    /// <summary>
    /// Draft, never final — every generator produces a reviewable draft. Nothing is emailed
    /// or published until a human approves it and triggers export.
    /// </summary>
    public string Status { get; set; } = "Draft";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RfpGenerationRequest
{
    public required string Title { get; set; }
    public required string Customer { get; set; }
    public string ReuseScope { get; set; } = "All"; // "All" | "Last12Months"
    public string Tone { get; set; } = "Formal";
    public required string UserObjectId { get; set; }

    /// <summary>Local | Online | All — which indexed corpus grounds generation.</summary>
    public string CorpusSource { get; set; } = CorpusSources.Online;

    /// <summary>Optional excerpt / notes from Knowledge Search “Use in RFP”.</summary>
    public string? FocusNotes { get; set; }
}

public class BusinessCaseGenerationRequest
{
    public required string Initiative { get; set; }
    public string Audience { get; set; } = "ExecutiveSponsor";
    public required string UserObjectId { get; set; }
    public string CorpusSource { get; set; } = CorpusSources.Online;

    /// <summary>Optional excerpt / notes from Knowledge Search “Use in Business Case”.</summary>
    public string? FocusNotes { get; set; }
}

public class ProposalGenerationRequest
{
    public required string Solution { get; set; }
    public bool IncludeDeck { get; set; } = true;
    public bool IncludeArchitectureDiagram { get; set; } = true;
    public required string UserObjectId { get; set; }
    public string CorpusSource { get; set; } = CorpusSources.Online;
}

/// <summary>Server-sent event payload for live RFP generation (section-by-section + tokens).</summary>
public class RfpStreamEvent
{
    /// <summary>outline | section-start | token | section-done | complete | error</summary>
    public required string Type { get; set; }

    public Guid? GenerationId { get; set; }

    public string? DocumentTitle { get; set; }

    public int? Order { get; set; }

    public string? Title { get; set; }

    /// <summary>Incremental text chunk from the model (Type = token).</summary>
    public string? Delta { get; set; }

    /// <summary>Full section body when Type = section-done.</summary>
    public string? Content { get; set; }

    public List<Citation>? Sources { get; set; }

    /// <summary>All section titles when Type = outline.</summary>
    public List<string>? SectionTitles { get; set; }

    public GeneratedDocument? Document { get; set; }

    public string? Message { get; set; }
}

