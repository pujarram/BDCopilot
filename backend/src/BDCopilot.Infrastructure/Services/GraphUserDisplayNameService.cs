using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using BDCopilot.Infrastructure.Graph;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace BDCopilot.Infrastructure.Services;

public sealed class GraphUserDisplayNameService : IGraphUserDisplayNameService
{
    private readonly BdCopilotDbContext _db;
    private readonly Graph.GraphClientFactory _graphFactory;
    private readonly ILogger<GraphUserDisplayNameService> _logger;

    public GraphUserDisplayNameService(
        BdCopilotDbContext db,
        Graph.GraphClientFactory graphFactory,
        ILogger<GraphUserDisplayNameService> logger)
    {
        _db = db;
        _graphFactory = graphFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveDisplayNamesAsync(
        IEnumerable<string> objectIds,
        CancellationToken ct = default)
    {
        var ids = objectIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && Guid.TryParse(id.Trim(), out _))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0) return result;

        var cached = await _db.PlannerUserCache.AsNoTracking()
            .Where(u => ids.Contains(u.ObjectId))
            .ToListAsync(ct);

        foreach (var c in cached)
        {
            result[c.ObjectId] = c.DisplayName;
        }

        var missing = ids.Where(id => !result.ContainsKey(id)).ToList();
        if (missing.Count == 0) return result;

        var graph = _graphFactory.GetClient();
        if (graph is null)
        {
            foreach (var id in missing)
            {
                result[id] = id;
            }

            return result;
        }

        foreach (var id in missing)
        {
            try
            {
                var user = await graph.Users[id].GetAsync(cancellationToken: ct);
                var name = user?.DisplayName ?? id;
                result[id] = name;

                var row = await _db.PlannerUserCache.FirstOrDefaultAsync(u => u.ObjectId == id, ct);
                if (row is null)
                {
                    _db.PlannerUserCache.Add(new PlannerUserCacheEntry
                    {
                        ObjectId = id,
                        DisplayName = name,
                        Mail = user?.Mail
                    });
                }
                else
                {
                    row.DisplayName = name;
                    row.Mail = user?.Mail;
                    row.RefreshedAt = DateTimeOffset.UtcNow;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve display name for {ObjectId}", id);
                result[id] = id;
            }
        }

        await _db.SaveChangesAsync(ct);
        return result;
    }
}
