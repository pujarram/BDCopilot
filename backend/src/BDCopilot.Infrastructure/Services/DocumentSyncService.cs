using System.Security.Cryptography;
using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using BDCopilot.Infrastructure.Parsing;
using BDCopilot.Infrastructure.Services;
using GraphClientFactory = BDCopilot.Infrastructure.Graph.GraphClientFactory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.Delta;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Pgvector;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Microsoft Graph delta sync: enumerates changed drive items per site, parses content,
/// computes ACL hashes, and upserts chunks + embeddings into Postgres/pgvector.
/// </summary>
public class DocumentSyncService : IDocumentSyncService
{
    private readonly BdCopilotDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly IDocumentParserRouter _parserRouter;
    private readonly GraphClientFactory _graphFactory;
    private readonly GraphSyncSettings _graphSettings;
    private readonly ILogger<DocumentSyncService> _logger;

    public DocumentSyncService(
        BdCopilotDbContext db,
        IEmbeddingService embeddings,
        IDocumentParserRouter parserRouter,
        GraphClientFactory graphFactory,
        IOptions<GraphSyncSettings> graphSettings,
        ILogger<DocumentSyncService> logger)
    {
        _db = db;
        _embeddings = embeddings;
        _parserRouter = parserRouter;
        _graphFactory = graphFactory;
        _graphSettings = graphSettings.Value;
        _logger = logger;
    }

    public async Task SyncAsync(CancellationToken ct = default)
    {
        var graph = _graphFactory.GetClient();
        if (graph is null)
        {
            _logger.LogInformation(
                "Graph credentials are not configured — skipping document sync. " +
                "Call SeedPilotDocumentsAsync for local demo data when AllowDevSeedWithoutGraph is true.");

            if (_graphSettings.AllowDevSeedWithoutGraph)
            {
                await SeedPilotDocumentsAsync(ct);
            }

            return;
        }

        var siteTargets = ResolveSiteTargets();
        if (siteTargets.Count == 0)
        {
            _logger.LogWarning(
                "No Graph site configured (Graph:PilotSiteId / Graph:PilotSitePath / Graph:SiteIds) — skipping sync.");
            return;
        }

        foreach (var target in siteTargets)
        {
            await SyncSiteAsync(graph, target.ConfigKey, target.SitePath, ct);
        }
    }

    private sealed record SiteSyncTarget(string ConfigKey, string? SitePath);

    public async Task<SyncHealthStatus> GetHealthAsync(CancellationToken ct = default)
    {
        var sites = await _db.SyncSiteStates.AsNoTracking().OrderBy(s => s.SiteId).ToListAsync(ct);
        var totalIndexed = sites.Sum(s => s.DocumentsIndexed);
        var lastSuccess = sites.Where(s => s.LastSuccessAt.HasValue).MaxBy(s => s.LastSuccessAt)?.LastSuccessAt;
        var lastAttempt = sites.Where(s => s.LastAttemptAt.HasValue).MaxBy(s => s.LastAttemptAt)?.LastAttemptAt;
        var lastError = sites.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.LastError))?.LastError;
        var configured = ResolveSiteIds().Count;

        var isHealthy = sites.Count > 0 &&
                        sites.All(s => !s.IsEnabled || (s.LastSuccessAt.HasValue && string.IsNullOrWhiteSpace(s.LastError)));

        return new SyncHealthStatus
        {
            LastSuccessAt = lastSuccess,
            LastAttemptAt = lastAttempt,
            LastError = lastError,
            DocumentsIndexed = totalIndexed,
            SitesConfigured = configured,
            IsHealthy = isHealthy,
            StatusMessage = isHealthy
                ? $"Sync healthy — {totalIndexed} documents indexed across {sites.Count} site(s)."
                : lastError ?? "Sync has not completed successfully yet.",
            Sites = sites.Select(s => new SyncSiteHealthItem
            {
                SiteId = s.SiteId,
                SiteDisplayName = s.SiteDisplayName,
                LastSuccessAt = s.LastSuccessAt,
                LastError = s.LastError,
                DocumentsIndexed = s.DocumentsIndexed,
                HasDeltaLink = !string.IsNullOrWhiteSpace(s.DeltaLink)
            }).ToList()
        };
    }

    public async Task<GraphProbeResult> ProbeGraphAsync(CancellationToken ct = default)
    {
        var result = new GraphProbeResult
        {
            GraphConfigured = _graphFactory.IsConfigured,
            TenantId = _graphSettings.TenantId,
            ClientIdSuffix = string.IsNullOrWhiteSpace(_graphSettings.ClientId)
                ? null
                : "…" + _graphSettings.ClientId[^Math.Min(8, _graphSettings.ClientId.Length)..],
            PilotSitePath = _graphSettings.PilotSitePath
        };

        if (!_graphFactory.IsConfigured)
        {
            result.Message =
                "Graph is not configured. Set Graph:TenantId, Graph:ClientId, Graph:ClientSecret (user-secrets).";
            result.Steps.Add("dotnet user-secrets set \"Graph:ClientSecret\" \"<secret-Value>\"");
            return result;
        }

        var graph = _graphFactory.GetClient();
        if (graph is null)
        {
            result.Message = "Failed to create GraphServiceClient.";
            return result;
        }

        result.TokenAcquired = true;
        result.Steps.Add("App-only token: OK");

        try
        {
            var targets = ResolveSiteTargets();
            if (targets.Count == 0)
            {
                result.Message = "No PilotSitePath / PilotSiteId configured.";
                return result;
            }

            var target = targets[0];
            var site = await ResolveGraphSiteAsync(graph, target.ConfigKey, target.SitePath, ct);
            result.ResolvedSiteId = site.Id;
            result.ResolvedSiteName = site.DisplayName;
            result.ResolvedSiteUrl = site.WebUrl;
            result.Steps.Add($"Site resolved: {site.DisplayName} ({site.Id})");

            var drives = await graph.Sites[site.Id!].Drives.GetAsync(cancellationToken: ct);
            result.DriveCount = drives?.Value?.Count ?? 0;
            result.Steps.Add($"Drives visible: {result.DriveCount}");

            var drive = drives?.Value?.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Id));
            if (drive?.Id is not null && _graphSettings.SyncFolderPaths.Count > 0)
            {
                var folder = _graphSettings.SyncFolderPaths[0].Trim().Trim('/');
                try
                {
                    var item = await graph.Drives[drive.Id].Root
                        .ItemWithPath(folder)
                        .GetAsync(cancellationToken: ct);
                    result.RfpFolderFound = item is not null;
                    result.Steps.Add(result.RfpFolderFound
                        ? $"Folder '{folder}' found under drive."
                        : $"Folder '{folder}' not found.");
                }
                catch (Exception folderEx)
                {
                    result.Steps.Add($"Folder '{folder}' check failed: {folderEx.Message}");
                }
            }

            result.Ok = true;
            result.Message = "Graph probe succeeded — Run sync now should work.";
            return result;
        }
        catch (Exception ex)
        {
            var detail = DescribeSyncFailure(ex);
            result.Message = detail;
            result.Steps.Add(detail);
            result.Steps.Add(
                "Fix: Entra app → API permissions → Application: Sites.Read.All + Files.Read.All → Grant admin consent. " +
                "Or Sites.Selected + grant the app on site BDTeam. " +
                "Graph Explorer uses YOUR user token — that can succeed while app-only sync still fails.");
            return result;
        }
    }

    public async Task ChunkAndEmbedAsync(
        Document document,
        IEnumerable<(string Text, string? Locator)> parsedSections,
        CancellationToken ct = default)
    {
        var existingChunks = await _db.DocumentChunks
            .Where(c => c.DocumentId == document.DocumentId)
            .ToListAsync(ct);
        if (existingChunks.Count > 0)
        {
            _db.DocumentChunks.RemoveRange(existingChunks);
        }

        if (_db.Entry(document).State == EntityState.Detached)
        {
            var tracked = await _db.Documents.FindAsync([document.DocumentId], ct);
            if (tracked is null)
            {
                _db.Documents.Add(document);
            }
            else
            {
                document = tracked;
            }
        }

        var index = 0;
        foreach (var (text, locator) in parsedSections)
        {
            foreach (var chunkText in SplitIntoChunks(text))
            {
                var vector = await _embeddings.EmbedAsync(chunkText, ct);
                _db.DocumentChunks.Add(new DocumentChunk
                {
                    DocumentId = document.DocumentId,
                    ChunkIndex = index++,
                    Content = chunkText,
                    Locator = locator,
                    Embedding = new Vector(vector)
                });
            }
        }

        document.IndexStatus = IndexStatus.Indexed;
        document.LastIndexedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task SeedPilotDocumentsAsync(CancellationToken ct = default)
    {
        if (!_graphSettings.AllowDevSeedWithoutGraph && !_graphFactory.IsConfigured)
        {
            _logger.LogInformation("SeedPilotDocumentsAsync skipped — AllowDevSeedWithoutGraph is false.");
            return;
        }

        var samples = new[]
        {
            (
                FileName: "Wealth_Proposal_Template.docx",
                FileType: "docx",
                TeamsChannel: "Wealth · Proposals",
                Url: "https://contoso.sharepoint.com/sites/wealth/Shared%20Documents/Wealth_Proposal_Template.docx",
                Sections: new[]
                {
                    ("Executive summary for wealth management proposal. Recommended sections: understanding of requirements, proposed solution and architecture, security and compliance, delivery timeline, and commercials and pricing. Use RAG with citations when drafting BD Copilot answers.", "Section 1"),
                    ("Scope covers portfolio review and advisory services. Commercial guidance: fixed-fee discovery then T&M delivery. Architecture points include Teams-embedded UI, Entra ID SSO, and SharePoint-grounded retrieval.", "Section 2")
                }
            ),
            (
                FileName: "Retail_Business_Case.pptx",
                FileType: "pptx",
                TeamsChannel: "Retail · Business Cases",
                Url: "https://contoso.sharepoint.com/sites/retail/Shared%20Documents/Retail_Business_Case.pptx",
                Sections: new[]
                {
                    ("Initiative: digital branch transformation. Benefits include reduced queue handling time, higher NPS, and unified advisor desktop. Risks and mitigations: change management and phased rollout.", "Slide 1"),
                    ("ROI projected at 18 months payback. Retail digital transformation business cases should quantify branch cost avoidance and cross-sell uplift.", "Slide 2")
                }
            ),
            (
                FileName: "Compliance_FAQ.pdf",
                FileType: "pdf",
                TeamsChannel: "Compliance · Knowledge",
                Url: "https://contoso.sharepoint.com/sites/compliance/Shared%20Documents/Compliance_FAQ.pdf",
                Sections: new[]
                {
                    ("All client communications require pre-approval. GDPR and data residency: keep EU client data in EU regions. Security and compliance topics for wealth proposals must reference existing SharePoint ACLs.", "Page 1"),
                    ("Data retention policy: 7 years minimum. SharePoint permission awareness: BD Copilot must never cite a file the caller cannot open. Indexed documents carry AclHash but live Graph permission checks enforce access on every query.", "Page 2")
                }
            )
        };

        var aclHash = ComputeAclHash(["dev-user", "dev-group-pilot"]);
        var indexed = 0;

        foreach (var sample in samples)
        {
            var driveItemId = $"seed-{sample.FileName}";
            var existing = await _db.Documents
                .FirstOrDefaultAsync(d => d.GraphDriveItemId == driveItemId, ct);

            if (existing is not null && existing.IndexStatus == IndexStatus.Indexed)
            {
                continue;
            }

            var doc = existing ?? new Document
            {
                FileName = sample.FileName,
                FileType = sample.FileType,
                Owner = "Pilot Seed",
                CreatedDate = DateTimeOffset.UtcNow.AddDays(-30),
                ModifiedDate = DateTimeOffset.UtcNow,
                TeamsChannel = sample.TeamsChannel,
                GraphDriveId = "seed-drive",
                GraphDriveItemId = driveItemId,
                SharePointUrl = sample.Url,
                AclHash = aclHash,
                IndexStatus = IndexStatus.Pending
            };

            if (existing is null)
            {
                _db.Documents.Add(doc);
                await _db.SaveChangesAsync(ct);
            }

            await ChunkAndEmbedAsync(doc, sample.Sections, ct);
            indexed++;
        }

        await EnsurePilotSiteStateAsync(indexed, ct);
        _logger.LogInformation("Seeded {Count} pilot documents for local demo.", indexed);
    }

    private async Task SyncSiteAsync(
        GraphServiceClient graph,
        string configKey,
        string? sitePath,
        CancellationToken ct)
    {
        var siteState = await EnsureSiteStateAsync(configKey, ct);
        siteState.LastAttemptAt = DateTimeOffset.UtcNow;
        siteState.LastError = null;

        try
        {
            var site = await ResolveGraphSiteAsync(graph, configKey, sitePath, ct);
            var graphSiteId = site.Id ?? configKey;
            siteState.SiteDisplayName = site.DisplayName;

            var drives = await graph.Sites[graphSiteId].Drives.GetAsync(cancellationToken: ct);
            var indexedThisRun = 0;

            foreach (var drive in drives?.Value ?? [])
            {
                if (string.IsNullOrWhiteSpace(drive.Id))
                {
                    continue;
                }

                indexedThisRun += await SyncDriveDeltaAsync(graph, siteState, drive, ct);
            }

            siteState.LastSuccessAt = DateTimeOffset.UtcNow;
            siteState.DocumentsIndexed = await _db.Documents.CountAsync(
                d => d.TeamsChannel.Contains(siteState.SiteDisplayName ?? configKey), ct);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Graph sync completed for site {SiteKey} ({GraphSiteId}): {Indexed} item(s) processed this run.",
                configKey, graphSiteId, indexedThisRun);
        }
        catch (Exception ex)
        {
            var description = DescribeSyncFailure(ex);
            siteState.LastError = description;
            await _db.SaveChangesAsync(ct);
            _logger.LogError(ex, "Graph sync failed for site {SiteKey}", configKey);
            throw new InvalidOperationException(description, ex);
        }
    }

    private async Task<Site> ResolveGraphSiteAsync(
        GraphServiceClient graph,
        string configKey,
        string? sitePath,
        CancellationToken ct)
    {
        ODataError? lastError = null;

        // Prefer hostname:/sites/Name — more reliable than composite ids with commas.
        foreach (var candidate in EnumerateSiteLookupKeys(sitePath, configKey))
        {
            try
            {
                Site? site;
                if (candidate.Contains(":/", StringComparison.Ordinal))
                {
                    // Path-style site keys need a site-item builder; Sites.WithUrl returns a collection.
                    var url = $"https://graph.microsoft.com/v1.0/sites/{candidate}";
                    site = await new Microsoft.Graph.Sites.Item.SiteItemRequestBuilder(url, graph.RequestAdapter)
                        .GetAsync(cancellationToken: ct);
                }
                else
                {
                    site = await graph.Sites[candidate].GetAsync(cancellationToken: ct);
                }

                if (site?.Id is not null)
                {
                    _logger.LogInformation("Resolved Graph site via {Lookup} → {SiteId} ({DisplayName})",
                        candidate, site.Id, site.DisplayName);
                    return site;
                }
            }
            catch (ODataError ex)
            {
                lastError = ex;
                _logger.LogWarning(
                    "Graph site lookup failed for {Lookup}: [{Code}] {Message}",
                    candidate,
                    ex.Error?.Code,
                    ex.Error?.Message ?? ex.Message);
            }
        }

        // Fallback: search by site name (helps when path lookup fails under some permission models).
        var searchName = ExtractSiteName(sitePath) ?? ExtractSiteName(configKey);
        if (!string.IsNullOrWhiteSpace(searchName))
        {
            try
            {
                var page = await graph.Sites.GetAsync(req =>
                {
                    req.QueryParameters.Search = $"\"{searchName}\"";
                }, ct);

                var match = page?.Value?.FirstOrDefault(s =>
                    string.Equals(s.Name, searchName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.DisplayName, searchName, StringComparison.OrdinalIgnoreCase)
                    || (s.WebUrl?.Contains($"/sites/{searchName}", StringComparison.OrdinalIgnoreCase) ?? false));

                if (match?.Id is not null)
                {
                    _logger.LogInformation(
                        "Resolved Graph site via search '{Search}' → {SiteId} ({DisplayName})",
                        searchName, match.Id, match.DisplayName);
                    return match;
                }

                _logger.LogWarning("Graph site search for '{Search}' returned {Count} hit(s) but no match.",
                    searchName, page?.Value?.Count ?? 0);
            }
            catch (ODataError ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Graph site search for '{Search}' failed.", searchName);
            }
        }

        throw new InvalidOperationException(DescribeGraphError(lastError), lastError);
    }

    private static string? ExtractSiteName(string? pathOrId)
    {
        if (string.IsNullOrWhiteSpace(pathOrId))
        {
            return null;
        }

        const string marker = "/sites/";
        var idx = pathOrId.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            return null;
        }

        var name = pathOrId[(idx + marker.Length)..].Trim().Trim('/');
        var slash = name.IndexOf('/');
        return slash > 0 ? name[..slash] : name;
    }

    private static IEnumerable<string> EnumerateSiteLookupKeys(string? sitePath, string configKey)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { sitePath, configKey })
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var key = raw.Trim().TrimStart('/');
            if (seen.Add(key))
            {
                yield return key;
            }
        }
    }

    private static string DescribeSyncFailure(Exception ex) =>
        ex switch
        {
            InvalidOperationException { InnerException: ODataError graphError } =>
                DescribeGraphError(graphError),
            ODataError graphError => DescribeGraphError(graphError),
            _ => ex.Message
        };

    private static string DescribeGraphError(ODataError? error)
    {
        if (error?.Error is null)
        {
            return error?.Message ?? "Microsoft Graph request failed.";
        }

        var code = error.Error.Code ?? "unknown";
        var message = error.Error.Message ?? error.Message ?? "Microsoft Graph request failed.";
        var detailText = error.Error.Details is { Count: > 0 } details
            ? string.Join(" ", details.Select(d => d.Message).Where(m => !string.IsNullOrWhiteSpace(m)))
            : string.Empty;

        var inner = error.Error.InnerError;
        var innerBits = new List<string>();
        if (inner?.AdditionalData is not null)
        {
            foreach (var key in new[] { "code", "message", "date", "request-id", "client-request-id" })
            {
                if (inner.AdditionalData.TryGetValue(key, out var val) && val is not null)
                {
                    innerBits.Add($"{key}={val}");
                }
            }
        }

        var hint = code switch
        {
            "accessDenied" or "Authorization_RequestDenied" =>
                " Grant APPLICATION permissions Sites.Read.All + Files.Read.All (or Sites.Selected + BDTeam site grant) and click Admin consent. User Graph Explorer success does not mean the app can read the site.",
            "generalException" =>
                " Almost always missing APPLICATION site permission or admin consent on the Entra app used by Graph:ClientId. Add Sites.Read.All + Files.Read.All (Application) → Grant admin consent for ikione.com, then restart API. Confirm PilotSitePath=ikione.sharepoint.com:/sites/BDTeam.",
            _ => string.Empty
        };

        var core = string.IsNullOrWhiteSpace(detailText)
            ? $"Graph error [{code}]: {message}."
            : $"Graph error [{code}]: {message} ({detailText}).";
        if (innerBits.Count > 0)
        {
            core += $" [{string.Join("; ", innerBits)}]";
        }

        return core + hint;
    }

    private async Task<int> SyncDriveDeltaAsync(
        GraphServiceClient graph,
        SyncSiteState siteState,
        Drive drive,
        CancellationToken ct)
    {
        var processed = 0;
        string? nextLink = siteState.DeltaLink;
        string? newDeltaLink = null;

        do
        {
            DeltaGetResponse? page;
            if (!string.IsNullOrWhiteSpace(nextLink) &&
                nextLink.Contains("/delta", StringComparison.OrdinalIgnoreCase))
            {
                page = await graph.Drives[drive.Id!].Items["root"]
                    .Delta
                    .WithUrl(nextLink)
                    .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }
            else
            {
                page = await graph.Drives[drive.Id!].Items["root"]
                    .Delta
                    .GetAsDeltaGetResponseAsync(cancellationToken: ct);
            }

            foreach (var item in page?.Value ?? [])
            {
                if (item.Id is null)
                {
                    continue;
                }

                if (item.AdditionalData.TryGetValue("@removed", out _))
                {
                    await RemoveDocumentByDriveItemIdAsync(item.Id, ct);
                    processed++;
                    continue;
                }

                if (item.File is null || string.IsNullOrWhiteSpace(item.Name))
                {
                    continue;
                }

                if (!IsUnderSyncFolderFilter(item))
                {
                    _logger.LogDebug(
                        "Skipping {Name} — outside Graph:SyncFolderPaths filter.",
                        item.Name);
                    continue;
                }

                var handled = await ProcessDriveItemAsync(graph, siteState, drive, item, ct);
                if (handled)
                {
                    processed++;
                }
            }

            nextLink = page?.OdataNextLink;
            newDeltaLink = page?.OdataDeltaLink ?? newDeltaLink;
        }
        while (!string.IsNullOrWhiteSpace(nextLink));

        if (!string.IsNullOrWhiteSpace(newDeltaLink))
        {
            siteState.DeltaLink = newDeltaLink;
        }

        return processed;
    }

    private async Task<bool> ProcessDriveItemAsync(
        GraphServiceClient graph,
        SyncSiteState siteState,
        Drive drive,
        DriveItem item,
        CancellationToken ct)
    {
        var extension = Path.GetExtension(item.Name!).TrimStart('.').ToLowerInvariant();
        if (extension is not ("docx" or "pptx" or "xlsx" or "pdf"))
        {
            _logger.LogDebug("Skipping unsupported file {Name}", item.Name);
            return false;
        }

        var aclHash = await ComputeAclHashFromGraphAsync(graph, drive.Id!, item.Id!, ct);
        var sharePointUrl = item.WebUrl ?? $"https://graph.microsoft.com/v1.0/drives/{drive.Id}/items/{item.Id}";

        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.GraphDriveItemId == item.Id, ct);

        var isNew = document is null;
        document ??= new Document
        {
            FileName = item.Name!,
            FileType = extension,
            TeamsChannel = siteState.SiteDisplayName ?? siteState.SiteId,
            SharePointUrl = sharePointUrl,
            GraphDriveId = drive.Id,
            GraphDriveItemId = item.Id,
            CreatedDate = item.CreatedDateTime ?? DateTimeOffset.UtcNow,
            ModifiedDate = item.LastModifiedDateTime ?? DateTimeOffset.UtcNow,
            IndexStatus = IndexStatus.Queued
        };

        document.FileName = item.Name!;
        document.FileType = extension;
        document.Owner = item.CreatedBy?.User?.DisplayName;
        document.ModifiedDate = item.LastModifiedDateTime ?? document.ModifiedDate;
        document.SharePointUrl = sharePointUrl;
        document.GraphDriveId = drive.Id;
        document.GraphDriveItemId = item.Id;
        document.AclHash = aclHash;

        if (isNew)
        {
            _db.Documents.Add(document);
            await _db.SaveChangesAsync(ct);
        }

        try
        {
            await using var contentStream = await graph.Drives[drive.Id!].Items[item.Id!].Content
                .GetAsync(cancellationToken: ct);
            if (contentStream is null)
            {
                document.IndexStatus = IndexStatus.Failed;
                await _db.SaveChangesAsync(ct);
                return false;
            }

            var parseResult = await _parserRouter.ParseAsync(item.Name!, contentStream, ct);
            document.IndexStatus = parseResult.IsMetadataOnly ? IndexStatus.MetadataOnly : IndexStatus.Queued;

            if (parseResult.IsMetadataOnly)
            {
                await _db.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Indexed {Name} as MetadataOnly: {Note}", item.Name, parseResult.Note);
            }

            await ChunkAndEmbedAsync(document, parseResult.Sections, ct);
            return true;
        }
        catch (Exception ex)
        {
            document.IndexStatus = IndexStatus.Failed;
            await _db.SaveChangesAsync(ct);
            _logger.LogWarning(ex, "Failed to index {Name}", item.Name);
            return false;
        }
    }

    private async Task RemoveDocumentByDriveItemIdAsync(string driveItemId, CancellationToken ct)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.GraphDriveItemId == driveItemId, ct);
        if (doc is null)
        {
            return;
        }

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<string?> ComputeAclHashFromGraphAsync(
        GraphServiceClient graph,
        string driveId,
        string itemId,
        CancellationToken ct)
    {
        try
        {
            var permissions = await graph.Drives[driveId].Items[itemId].Permissions
                .GetAsync(cancellationToken: ct);

            var principalIds = new List<string>();
            foreach (var permission in permissions?.Value ?? [])
            {
                if (permission.GrantedToV2?.User?.Id is { } userId)
                {
                    principalIds.Add(userId);
                }

                if (permission.GrantedToV2?.Group?.Id is { } groupId)
                {
                    principalIds.Add(groupId);
                }

                foreach (var identity in permission.GrantedToIdentitiesV2 ?? [])
                {
                    if (identity.User?.Id is { } uid)
                    {
                        principalIds.Add(uid);
                    }

                    if (identity.Group?.Id is { } gid)
                    {
                        principalIds.Add(gid);
                    }
                }

                if (permission.Link?.Scope is not null)
                {
                    principalIds.Add($"link:{permission.Link.Scope}");
                }
            }

            return principalIds.Count == 0 ? null : ComputeAclHash(principalIds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read permissions for drive {DriveId} item {ItemId}", driveId, itemId);
            return null;
        }
    }

    private static string ComputeAclHash(IEnumerable<string> principalIds)
    {
        var sorted = principalIds.Where(id => !string.IsNullOrWhiteSpace(id)).OrderBy(id => id, StringComparer.Ordinal);
        var payload = string.Join("|", sorted);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private List<string> ResolveSiteIds()
    {
        return ResolveSiteTargets().Select(t => t.ConfigKey).ToList();
    }

    private List<SiteSyncTarget> ResolveSiteTargets()
    {
        var targets = new List<SiteSyncTarget>();

        if (!string.IsNullOrWhiteSpace(_graphSettings.PilotSitePath) ||
            !string.IsNullOrWhiteSpace(_graphSettings.PilotSiteId))
        {
            var configKey = string.IsNullOrWhiteSpace(_graphSettings.PilotSiteId)
                ? _graphSettings.PilotSitePath.Trim()
                : _graphSettings.PilotSiteId.Trim();

            targets.Add(new SiteSyncTarget(
                configKey,
                string.IsNullOrWhiteSpace(_graphSettings.PilotSitePath)
                    ? null
                    : _graphSettings.PilotSitePath.Trim()));
        }

        foreach (var siteId in _graphSettings.SiteIds.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            if (!targets.Any(t => t.ConfigKey.Equals(siteId, StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(new SiteSyncTarget(siteId.Trim(), null));
            }
        }

        return targets;
    }

    /// <summary>
    /// When <see cref="GraphSyncSettings.SyncFolderPaths"/> is set, keep only files under those
    /// library-relative folders (e.g. General/RFP DataBase/IWM). Removals are handled earlier
    /// and are not filtered here.
    /// </summary>
    private bool IsUnderSyncFolderFilter(DriveItem item)
    {
        var prefixes = _graphSettings.SyncFolderPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizeLibraryRelativePath)
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (prefixes.Count == 0)
        {
            return true;
        }

        var relative = GetLibraryRelativePath(item);
        if (relative is null)
        {
            return false;
        }

        return prefixes.Any(prefix =>
            relative.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            relative.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a path relative to the drive root, e.g. <c>General/RFP DataBase/IWM/file.docx</c>.
    /// Graph ParentReference.Path is typically <c>/drives/{id}/root:/General/RFP DataBase/IWM</c>.
    /// </summary>
    private static string? GetLibraryRelativePath(DriveItem item)
    {
        var parentPath = item.ParentReference?.Path;
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            // Item at drive root (or delta omitted path) — only the file name.
            return string.IsNullOrWhiteSpace(item.Name) ? null : NormalizeLibraryRelativePath(item.Name!);
        }

        const string rootMarker = "/root:";
        var idx = parentPath.IndexOf(rootMarker, StringComparison.OrdinalIgnoreCase);
        string folderRel;
        if (idx >= 0)
        {
            folderRel = parentPath[(idx + rootMarker.Length)..];
        }
        else if (parentPath.StartsWith("root:", StringComparison.OrdinalIgnoreCase))
        {
            folderRel = parentPath[5..];
        }
        else
        {
            // Unexpected shape — do not guess when a folder filter is active.
            return null;
        }

        folderRel = NormalizeLibraryRelativePath(folderRel);
        var fileName = item.Name?.Trim() ?? "";
        if (fileName.Length == 0)
        {
            return folderRel.Length == 0 ? null : folderRel;
        }

        return folderRel.Length == 0
            ? NormalizeLibraryRelativePath(fileName)
            : NormalizeLibraryRelativePath($"{folderRel}/{fileName}");
    }

    private static string NormalizeLibraryRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        // Allow configs that accidentally include the library display name.
        const string sharedDocs = "Shared Documents/";
        if (normalized.StartsWith(sharedDocs, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[sharedDocs.Length..];
        }

        const string documents = "Documents/";
        if (normalized.StartsWith(documents, StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[documents.Length..];
        }

        return normalized;
    }

    private async Task<SyncSiteState> EnsureSiteStateAsync(string siteId, CancellationToken ct)
    {
        var state = await _db.SyncSiteStates.FirstOrDefaultAsync(s => s.SiteId == siteId, ct);
        if (state is not null)
        {
            return state;
        }

        state = new SyncSiteState { SiteId = siteId };
        _db.SyncSiteStates.Add(state);
        await _db.SaveChangesAsync(ct);
        return state;
    }

    private async Task EnsurePilotSiteStateAsync(int indexed, CancellationToken ct)
    {
        var siteId = string.IsNullOrWhiteSpace(_graphSettings.PilotSiteId)
            ? "local-pilot"
            : _graphSettings.PilotSiteId;

        var state = await EnsureSiteStateAsync(siteId, ct);
        state.SiteDisplayName ??= "Local Pilot";
        state.LastSuccessAt = DateTimeOffset.UtcNow;
        state.LastAttemptAt = DateTimeOffset.UtcNow;
        state.DocumentsIndexed = indexed;
        state.LastError = null;
        await _db.SaveChangesAsync(ct);
    }

    private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize = 1200, int overlap = 200)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(chunkSize, text.Length - start);
            yield return text.Substring(start, length);
            if (start + length >= text.Length)
            {
                yield break;
            }

            start += chunkSize - overlap;
        }
    }
}
