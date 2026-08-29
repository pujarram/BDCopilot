using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

/// <summary>
/// Nearest-neighbour search over document_chunks, already trimmed to what the calling user
/// is allowed to see. Implementations must call <see cref="IAccessControlService"/> before
/// returning any chunk — see the "why security trimming happens twice" note in the
/// architecture doc.
/// </summary>
public interface IVectorSearchService
{
    /// <param name="corpusSource">Local | Online | All — filters which indexed corpus is searched.</param>
    Task<List<SearchResultItem>> SearchAsync(
        string query,
        string userObjectId,
        int topK = 8,
        string? corpusSource = null,
        CancellationToken ct = default);
}

/// <summary>Indexes files from the local docs folder (corpus source = Local).</summary>
public interface ILocalDocumentSyncService
{
    Task<LocalDocsSyncResult> SyncAsync(CancellationToken ct = default);

    string ResolveRootPath();
}

/// <summary>
/// Decides, for a specific user, which chunks/documents they are currently allowed to open.
/// When <c>AzureAd:EnforceAcl</c> is true, performs live Microsoft Graph permission checks
/// on every query — not cached from index time.
/// </summary>
public interface IAccessControlService
{
    Task<bool> CanUserAccessDocumentAsync(string userObjectId, Guid documentId, CancellationToken ct = default);

    Task<IReadOnlyCollection<Guid>> FilterAccessibleDocumentIdsAsync(
        string userObjectId, IEnumerable<Guid> documentIds, CancellationToken ct = default);
}

/// <summary>Generates and stores embeddings; used by both the sync pipeline and ad-hoc search.</summary>
public interface IEmbeddingService
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

public interface IBlobStorageService
{
    /// <summary>Uploads a generated document (docx/pptx) to the blob cache and returns a short-lived SAS URL
    /// (or a local API download path when Azurite is unavailable).</summary>
    Task<string> UploadGeneratedFileAsync(string fileName, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Opens a locally persisted export by download token, or null if not found.</summary>
    Task<(Stream Content, string ContentType, string FileName)?> TryOpenLocalExportAsync(string token, CancellationToken ct = default);
}

/// <summary>Pulls changed files from SharePoint libraries via Microsoft Graph delta query,
/// parses them, and writes chunks + embeddings + metadata.</summary>
public interface IDocumentSyncService
{
    Task SyncAsync(CancellationToken ct = default);

    Task<SyncHealthStatus> GetHealthAsync(CancellationToken ct = default);

    /// <summary>Diagnose Graph auth + site access without indexing files.</summary>
    Task<GraphProbeResult> ProbeGraphAsync(CancellationToken ct = default);

    Task ChunkAndEmbedAsync(
        Document document,
        IEnumerable<(string Text, string? Locator)> parsedSections,
        CancellationToken ct = default);

    Task SeedPilotDocumentsAsync(CancellationToken ct = default);
}

/// <summary>Routes a file stream to the correct format-specific parser by extension.</summary>
public interface IDocumentParserRouter
{
    Task<DocumentParseResult> ParseAsync(string fileName, Stream content, CancellationToken ct = default);
}

/// <summary>Persists token-usage rows for cost attribution (Phase 4).</summary>
public interface ITokenUsageTracker
{
    Task TrackAsync(TokenUsageRecord record, CancellationToken ct = default);
}

/// <summary>Audit trail for document access decisions (Phase 2+).</summary>
public interface IAccessAuditService
{
    Task LogAsync(AccessAuditRecord record, CancellationToken ct = default);

    Task<IReadOnlyList<AccessAuditRecord>> ListRecentAsync(int limit = 50, CancellationToken ct = default);
}
