using System.Security.Cryptography;
using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Indexes files under the configured local docs folder into the same Document / DocumentChunk
/// tables used by SharePoint sync, stamped with GraphDriveId = local-docs.
/// </summary>
public class LocalDocumentSyncService : ILocalDocumentSyncService
{
    private readonly BdCopilotDbContext _db;
    private readonly IDocumentParserRouter _parserRouter;
    private readonly IDocumentSyncService _documentSync;
    private readonly LocalDocsSettings _settings;
    private readonly IHostEnvironment _env;
    private readonly ILogger<LocalDocumentSyncService> _logger;

    public LocalDocumentSyncService(
        BdCopilotDbContext db,
        IDocumentParserRouter parserRouter,
        IDocumentSyncService documentSync,
        IOptions<LocalDocsSettings> settings,
        IHostEnvironment env,
        ILogger<LocalDocumentSyncService> logger)
    {
        _db = db;
        _parserRouter = parserRouter;
        _documentSync = documentSync;
        _settings = settings.Value;
        _env = env;
        _logger = logger;
    }

    public string ResolveRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.RootPath))
        {
            var configured = _settings.RootPath.Trim();
            return Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(_env.ContentRootPath, configured));
        }

        // Api content root: backend/src/BDCopilot.Api → repo docs/
        return Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "..", "docs"));
    }

    public async Task<LocalDocsSyncResult> SyncAsync(CancellationToken ct = default)
    {
        var root = ResolveRootPath();
        var result = new LocalDocsSyncResult
        {
            RootPath = root,
            RootExists = Directory.Exists(root)
        };

        if (!result.RootExists)
        {
            result.StatusMessage = $"Local docs folder not found: {root}";
            _logger.LogWarning("{Message}", result.StatusMessage);
            return result;
        }

        var extensions = new HashSet<string>(
            _settings.Extensions.Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        var exclude = new HashSet<string>(_settings.ExcludeFolderNames, StringComparer.OrdinalIgnoreCase);

        var files = EnumerateFiles(root, extensions, exclude).ToList();
        result.FilesFound = files.Count;

        foreach (var filePath in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var outcome = await IndexFileAsync(root, filePath, ct);
                if (outcome == IndexOutcome.Indexed) result.Indexed++;
                else if (outcome == IndexOutcome.Skipped) result.SkippedUnchanged++;
            }
            catch (Exception ex)
            {
                result.Failed++;
                var msg = $"{Path.GetFileName(filePath)}: {ex.Message}";
                result.Errors.Add(msg);
                _logger.LogWarning(ex, "Failed to index local file {Path}", filePath);
            }
        }

        result.StatusMessage = result.FilesFound == 0
            ? $"No supported files under {root}. Drop .docx/.pdf/.pptx/.xlsx/.txt/.md there (excluding teams-app)."
            : $"Local index: {result.Indexed} indexed, {result.SkippedUnchanged} unchanged, {result.Failed} failed ({result.FilesFound} found).";

        _logger.LogInformation("{Message}", result.StatusMessage);
        return result;
    }

    private enum IndexOutcome { Indexed, Skipped }

    private async Task<IndexOutcome> IndexFileAsync(string root, string filePath, CancellationToken ct)
    {
        var info = new FileInfo(filePath);
        var relative = Path.GetRelativePath(root, filePath).Replace('\\', '/');
        var driveItemId = "local:" + Sha256Hex(relative);
        var fileType = info.Extension.TrimStart('.').ToLowerInvariant();
        var channel = "Local · " + (Path.GetDirectoryName(relative)?.Replace('\\', '/') is { Length: > 0 } dir
            ? dir
            : "docs");

        var existing = await _db.Documents
            .FirstOrDefaultAsync(d => d.GraphDriveId == CorpusSources.LocalDriveId && d.GraphDriveItemId == driveItemId, ct);

        if (existing is not null
            && existing.IndexStatus == IndexStatus.Indexed
            && existing.ModifiedDate >= info.LastWriteTimeUtc)
        {
            return IndexOutcome.Skipped;
        }

        await using var stream = File.OpenRead(filePath);
        var parsed = await _parserRouter.ParseAsync(info.Name, stream, ct);
        if (parsed.Sections.Count == 0)
        {
            throw new InvalidOperationException("Parser returned no sections.");
        }

        var doc = existing ?? new Document
        {
            FileName = info.Name,
            FileType = fileType,
            Owner = Environment.UserName,
            CreatedDate = info.CreationTimeUtc,
            ModifiedDate = info.LastWriteTimeUtc,
            TeamsChannel = channel,
            GraphDriveId = CorpusSources.LocalDriveId,
            GraphDriveItemId = driveItemId,
            SharePointUrl = new Uri(filePath).AbsoluteUri,
            AclHash = "local-open",
            IndexStatus = IndexStatus.Pending
        };

        if (existing is null)
        {
            _db.Documents.Add(doc);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            existing.FileName = info.Name;
            existing.FileType = fileType;
            existing.ModifiedDate = info.LastWriteTimeUtc;
            existing.TeamsChannel = channel;
            existing.SharePointUrl = new Uri(filePath).AbsoluteUri;
            doc = existing;
        }

        await _documentSync.ChunkAndEmbedAsync(doc, parsed.Sections, ct);

        return IndexOutcome.Indexed;
    }

    private static IEnumerable<string> EnumerateFiles(
        string root,
        HashSet<string> extensions,
        HashSet<string> excludeFolders)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(dir);
            }
            catch
            {
                continue;
            }

            foreach (var sub in subDirs)
            {
                var name = Path.GetFileName(sub);
                if (excludeFolders.Contains(name)) continue;
                stack.Push(sub);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (extensions.Contains(Path.GetExtension(file)))
                {
                    yield return file;
                }
            }
        }
    }

    private static string Sha256Hex(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
