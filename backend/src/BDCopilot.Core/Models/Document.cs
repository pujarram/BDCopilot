namespace BDCopilot.Core.Models;

/// <summary>
/// Indexing state of a source file, surfaced to the Document Library screen.
/// </summary>
public enum IndexStatus
{
    Pending,
    Queued,
    Indexed,
    MetadataOnly,
    SkippedNoReadGrant,
    Failed
}

/// <summary>
/// A file discovered under a Teams channel's SharePoint document library.
/// Matches the metadata schema in the architecture brief: DocumentId, FileName, FileType,
/// Owner, CreatedDate, ModifiedDate, TeamsChannel, SharePointUrl, plus an AclHash used to
/// short-circuit permission re-checks between full Graph calls.
/// </summary>
public class Document
{
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    public required string FileName { get; set; }

    /// <summary>File extension without the dot, e.g. "docx", "pptx", "xlsx", "pdf".</summary>
    public required string FileType { get; set; }

    public string? Owner { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public DateTimeOffset ModifiedDate { get; set; }

    /// <summary>Human-readable "Team · Channel" path, e.g. "Wealth · Proposals".</summary>
    public required string TeamsChannel { get; set; }

    /// <summary>The Microsoft Graph drive id containing this file.</summary>
    public string? GraphDriveId { get; set; }

    /// <summary>The Microsoft Graph driveItem id backing this file.</summary>
    public string? GraphDriveItemId { get; set; }

    public required string SharePointUrl { get; set; }

    /// <summary>
    /// Fingerprint of the last known SharePoint permission set. This is a caching hint only —
    /// the API re-checks the live ACL through Graph on every query. See
    /// <see cref="Interfaces.IAccessControlService"/>.
    /// </summary>
    public string? AclHash { get; set; }

    public IndexStatus IndexStatus { get; set; } = IndexStatus.Pending;

    public DateTimeOffset? LastIndexedAt { get; set; }

    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
}
