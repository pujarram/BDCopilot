namespace BDCopilot.Core.Models;

public class SearchRequest
{
    public required string Query { get; set; }
    public required string UserObjectId { get; set; }
    public int TopK { get; set; } = 8;
}

public class SearchResultItem
{
    public required Citation Source { get; set; }
    public required string Excerpt { get; set; }
    public double Score { get; set; }
}

public class DocumentListItem
{
    public Guid DocumentId { get; set; }
    public required string FileName { get; set; }
    public required string FileType { get; set; }
    public required string TeamsChannel { get; set; }
    public DateTimeOffset ModifiedDate { get; set; }
    public bool AccessibleToCaller { get; set; }
    public required string IndexStatus { get; set; }

    /// <summary>Local | Online</summary>
    public string CorpusSource { get; set; } = CorpusSources.Online;
}

/// <summary>Knowledge Search: retrieve + grounded short answer.</summary>
public class KnowledgeSearchRequest
{
    public required string Query { get; set; }
    public required string UserObjectId { get; set; }
    public int TopK { get; set; } = 8;
    public string CorpusSource { get; set; } = CorpusSources.Online;
}

public class KnowledgeSearchResponse
{
    public required string Query { get; set; }
    public required string Answer { get; set; }
    public List<Citation> Citations { get; set; } = [];
    public List<SearchResultItem> Results { get; set; } = [];
    public required string AiProvider { get; set; }
    public required string Model { get; set; }
}
