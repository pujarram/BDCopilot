using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using BDCopilot.Infrastructure.Services;
using GraphClientFactory = BDCopilot.Infrastructure.Graph.GraphClientFactory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Live Microsoft Graph permission checks when <c>AzureAd:EnforceAcl</c> is true;
/// dev bypass when Graph is unavailable or ACL enforcement is disabled.
/// </summary>
public class AccessControlService : IAccessControlService
{
    private static readonly HashSet<string> ReadRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "read", "write", "owner", "sp.view", "sp.edit", "sp.manage", "sp.full control"
    };

    private readonly BdCopilotDbContext _db;
    private readonly GraphClientFactory _graphFactory;
    private readonly GraphSyncSettings _graphSettings;
    private readonly AzureAdSettings _azureAdSettings;
    private readonly IAccessAuditService _audit;
    private readonly ILogger<AccessControlService> _logger;

    public AccessControlService(
        BdCopilotDbContext db,
        GraphClientFactory graphFactory,
        IOptions<GraphSyncSettings> graphSettings,
        IOptions<AzureAdSettings> azureAdSettings,
        IAccessAuditService audit,
        ILogger<AccessControlService> logger)
    {
        _db = db;
        _graphFactory = graphFactory;
        _graphSettings = graphSettings.Value;
        _azureAdSettings = azureAdSettings.Value;
        _audit = audit;
        _logger = logger;
    }

    public async Task<bool> CanUserAccessDocumentAsync(string userObjectId, Guid documentId, CancellationToken ct = default)
    {
        var doc = await _db.Documents.AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocumentId == documentId, ct);

        if (doc is null)
        {
            await LogDecisionAsync(userObjectId, documentId, false, "Document not found", ct);
            return false;
        }

        var allowed = await EvaluateAccessAsync(userObjectId, doc, ct);
        await LogDecisionAsync(userObjectId, documentId, allowed,
            allowed ? "Access granted" : "Access denied by ACL check", ct);
        return allowed;
    }

    public async Task<IReadOnlyCollection<Guid>> FilterAccessibleDocumentIdsAsync(
        string userObjectId, IEnumerable<Guid> documentIds, CancellationToken ct = default)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        if (!ShouldEnforceLiveAcl())
        {
            var existing = await _db.Documents
                .Where(d => ids.Contains(d.DocumentId))
                .Select(d => d.DocumentId)
                .ToListAsync(ct);

            _logger.LogDebug(
                "ACL dev bypass for user {UserObjectId}: {Count}/{Requested} documents allowed.",
                userObjectId, existing.Count, ids.Count);

            return existing;
        }

        var docs = await _db.Documents.AsNoTracking()
            .Where(d => ids.Contains(d.DocumentId))
            .ToListAsync(ct);

        var allowed = new List<Guid>();
        foreach (var doc in docs)
        {
            if (await EvaluateAccessAsync(userObjectId, doc, ct))
            {
                allowed.Add(doc.DocumentId);
            }
        }

        return allowed;
    }

    private bool ShouldEnforceLiveAcl() =>
        _azureAdSettings.EnforceAcl && _graphFactory.IsConfigured;

    private bool AllowDevBypass() =>
        !_azureAdSettings.EnforceAcl || !_graphFactory.IsConfigured
            ? _graphSettings.AllowDevBypass
            : false;

    private async Task<bool> EvaluateAccessAsync(string userObjectId, Document doc, CancellationToken ct)
    {
        // Local disk corpus is readable by any authenticated app user (dev machine / shared docs folder).
        if (CorpusSources.IsLocalDocument(doc) || CorpusSources.IsPlannerDocument(doc))
        {
            return true;
        }

        if (!ShouldEnforceLiveAcl())
        {
            if (AllowDevBypass())
            {
                return true;
            }

            _logger.LogWarning(
                "ACL enforcement disabled but AllowDevBypass=false — denying access to {DocumentId}.",
                doc.DocumentId);
            return false;
        }

        if (string.IsNullOrWhiteSpace(doc.GraphDriveId) || string.IsNullOrWhiteSpace(doc.GraphDriveItemId))
        {
            _logger.LogWarning(
                "Document {DocumentId} missing Graph drive metadata — denying under EnforceAcl.",
                doc.DocumentId);
            return false;
        }

        var graph = _graphFactory.GetClient();
        if (graph is null)
        {
            return false;
        }

        return await UserHasReadViaGraphAsync(
            graph, doc.GraphDriveId, doc.GraphDriveItemId, userObjectId, ct);
    }

    private async Task<bool> UserHasReadViaGraphAsync(
        GraphServiceClient graph,
        string driveId,
        string itemId,
        string userObjectId,
        CancellationToken ct)
    {
        try
        {
            var permissions = await graph.Drives[driveId].Items[itemId].Permissions
                .GetAsync(cancellationToken: ct);

            foreach (var permission in permissions?.Value ?? [])
            {
                if (!PermissionGrantsRead(permission))
                {
                    continue;
                }

                if (permission.GrantedToV2?.User?.Id?.Equals(userObjectId, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }

                foreach (var identity in permission.GrantedToIdentitiesV2 ?? [])
                {
                    if (identity.User?.Id?.Equals(userObjectId, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return true;
                    }
                }

                if (permission.Link?.Scope is string scope &&
                    (scope.Equals("anonymous", StringComparison.OrdinalIgnoreCase) ||
                     scope.Equals("organization", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Live Graph ACL check failed for user {User} on drive {DriveId} item {ItemId} — denying.",
                userObjectId, driveId, itemId);
            return false;
        }
    }

    private static bool PermissionGrantsRead(Permission permission)
    {
        if (permission.Roles is { Count: > 0 } roles)
        {
            return roles.Any(r => ReadRoles.Contains(r));
        }

        return permission.Link is not null;
    }

    private async Task LogDecisionAsync(
        string userObjectId, Guid documentId, bool allowed, string reason, CancellationToken ct)
    {
        try
        {
            await _audit.LogAsync(new AccessAuditRecord
            {
                UserObjectId = userObjectId,
                DocumentId = documentId,
                Allowed = allowed,
                Reason = reason
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write access audit record.");
        }
    }
}
