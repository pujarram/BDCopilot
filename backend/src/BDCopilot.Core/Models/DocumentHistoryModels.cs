using System.Text.Json;
using BDCopilot.Core.Models;

namespace BDCopilot.Core.Models;

/// <summary>
/// Shared generation history for Business Case / Proposal (and optionally other types).
/// Sections stored as JSON so section counts can differ by document type.
/// </summary>
public class GeneratedDocumentHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GenerationId { get; set; }

    /// <summary>BusinessCase | Proposal</summary>
    public required string DocumentType { get; set; }

    public required string Title { get; set; }

    public string Status { get; set; } = "Draft";

    /// <summary>Type-specific fields (initiative, audience, solution, …) as JSON.</summary>
    public string MetadataJson { get; set; } = "{}";

    /// <summary>Serialized List&lt;GeneratedSection&gt;.</summary>
    public string SectionsJson { get; set; } = "[]";

    public required string CreatedByUserObjectId { get; set; }

    public string? CreatedByDisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? ChannelSharePointUrl { get; set; }

    public string? ChannelUploadStatus { get; set; }

    public string? ChannelUploadError { get; set; }

    public List<GeneratedSection> GetSections()
    {
        try
        {
            return JsonSerializer.Deserialize<List<GeneratedSection>>(SectionsJson,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void SetSections(IEnumerable<GeneratedSection> sections)
    {
        SectionsJson = JsonSerializer.Serialize(sections.ToList(),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    public Dictionary<string, string?> GetMetadata()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(MetadataJson,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new Dictionary<string, string?>();
        }
        catch
        {
            return new Dictionary<string, string?>();
        }
    }

    public void SetMetadata(IDictionary<string, string?> metadata)
    {
        MetadataJson = JsonSerializer.Serialize(metadata,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}

public class SaveGeneratedDocumentHistoryRequest
{
    /// <summary>
    /// Optional in the JSON body — the controller always sets this from the route
    /// (<c>/api/history/{documentType}/save</c>).
    /// </summary>
    public string? DocumentType { get; set; }

    public required GeneratedDocument Document { get; set; }
    public required string UserObjectId { get; set; }
    public string? DisplayName { get; set; }
    public Dictionary<string, string?>? Metadata { get; set; }
}

public class SaveGeneratedDocumentToChannelRequest
{
    public Guid HistoryId { get; set; }
    public required string UserObjectId { get; set; }
}

public class GeneratedDocumentHistoryListItem
{
    public Guid Id { get; set; }
    public Guid GenerationId { get; set; }
    public required string DocumentType { get; set; }
    public required string Title { get; set; }
    public string Status { get; set; } = "Draft";
    public required string CreatedByUserObjectId { get; set; }
    public string? CreatedByDisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? ChannelUploadStatus { get; set; }
    public string? ChannelSharePointUrl { get; set; }
    public string? SummaryLabel { get; set; }
}
