using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Api.Controllers;

/// <summary>Document Library — every file the sync service has indexed, with its source
/// channel and the caller's access level.</summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DocumentsController : ControllerBase
{
    private readonly BdCopilotDbContext _db;
    private readonly IAccessControlService _accessControl;
    private readonly ILocalDocumentSyncService _localSync;

    public DocumentsController(
        BdCopilotDbContext db,
        IAccessControlService accessControl,
        ILocalDocumentSyncService localSync)
    {
        _db = db;
        _accessControl = accessControl;
        _localSync = localSync;
    }

    /// <param name="userObjectId">Entra ID object id of the caller.</param>
    /// <param name="corpusSource">Local | Online | All</param>
    [HttpGet]
    [ProducesResponseType(typeof(List<DocumentListItem>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DocumentListItem>>> List(
        [FromQuery] string userObjectId,
        [FromQuery] string? corpusSource = null,
        CancellationToken ct = default)
    {
        var source = string.IsNullOrWhiteSpace(corpusSource)
            ? CorpusSources.All
            : CorpusSources.Normalize(corpusSource);
        var documentsQuery = _db.Documents.AsQueryable();

        documentsQuery = source switch
        {
            CorpusSources.Local => documentsQuery.Where(d => d.GraphDriveId == CorpusSources.LocalDriveId),
            CorpusSources.Online => documentsQuery.Where(d => d.GraphDriveId != CorpusSources.LocalDriveId),
            _ => documentsQuery
        };

        var documents = await documentsQuery
            .OrderByDescending(d => d.ModifiedDate)
            .ToListAsync(ct);

        var accessibleIds = await _accessControl.FilterAccessibleDocumentIdsAsync(
            userObjectId, documents.Select(d => d.DocumentId), ct);

        var items = documents.Select(d => new DocumentListItem
        {
            DocumentId = d.DocumentId,
            FileName = d.FileName,
            FileType = d.FileType,
            TeamsChannel = d.TeamsChannel,
            ModifiedDate = d.ModifiedDate,
            AccessibleToCaller = accessibleIds.Contains(d.DocumentId),
            IndexStatus = d.IndexStatus.ToString(),
            CorpusSource = CorpusSources.IsLocalDocument(d) ? CorpusSources.Local : CorpusSources.Online
        }).ToList();

        return Ok(items);
    }

    /// <summary>
    /// Open a cited document in a new tab: Local files are streamed from disk;
    /// Online documents redirect to SharePoint.
    /// </summary>
    [HttpGet("{documentId:guid}/open")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Open(
        Guid documentId,
        [FromQuery] string userObjectId,
        CancellationToken ct = default)
    {
        var doc = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, ct);

        if (doc is null)
        {
            return NotFound();
        }

        if (!await _accessControl.CanUserAccessDocumentAsync(userObjectId, documentId, ct))
        {
            return Forbid();
        }

        if (CorpusSources.IsLocalDocument(doc))
        {
            return OpenLocalFile(doc);
        }

        if (string.IsNullOrWhiteSpace(doc.SharePointUrl) ||
            !Uri.TryCreate(doc.SharePointUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return NotFound(new { title = "No openable URL for this document." });
        }

        return Redirect(doc.SharePointUrl);
    }

    private IActionResult OpenLocalFile(Document doc)
    {
        if (!TryResolveLocalPath(doc.SharePointUrl, out var path) || !System.IO.File.Exists(path))
        {
            return NotFound(new { title = "Local file not found on disk.", detail = doc.FileName });
        }

        var root = Path.GetFullPath(_localSync.ResolveRootPath());
        var full = Path.GetFullPath(path);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { title = "File is outside the configured LocalDocs root." });
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(doc.FileName, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        // Attachment so Office files download reliably when opened in a new tab.
        return PhysicalFile(full, contentType, fileDownloadName: doc.FileName);
    }

    private static bool TryResolveLocalPath(string? sharePointUrl, out string path)
    {
        path = "";
        if (string.IsNullOrWhiteSpace(sharePointUrl))
        {
            return false;
        }

        if (Uri.TryCreate(sharePointUrl, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            path = Uri.UnescapeDataString(uri.LocalPath);
            return true;
        }

        if (Path.IsPathRooted(sharePointUrl) && System.IO.File.Exists(sharePointUrl))
        {
            path = sharePointUrl;
            return true;
        }

        return false;
    }
}
