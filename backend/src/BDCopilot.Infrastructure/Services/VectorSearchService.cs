using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Two-step retrieval: ask Postgres for the nearest chunks by cosine distance, then trim the
/// result to documents the caller can actually open. The ACL filter runs after the vector
/// search (cheap re-check against a small candidate set) rather than before it (which would
/// mean joining against a live permissions call for every row in the index).
/// </summary>
public class VectorSearchService : IVectorSearchService
{
    private readonly BdCopilotDbContext _db;
    private readonly IEmbeddingService _embeddings;
    private readonly IAccessControlService _accessControl;

    public VectorSearchService(BdCopilotDbContext db, IEmbeddingService embeddings, IAccessControlService accessControl)
    {
        _db = db;
        _embeddings = embeddings;
        _accessControl = accessControl;
    }

    public async Task<List<SearchResultItem>> SearchAsync(
        string query,
        string userObjectId,
        int topK = 8,
        string? corpusSource = null,
        CancellationToken ct = default)
    {
        var queryEmbedding = await _embeddings.EmbedAsync(query, ct);
        var queryVector = new Vector(queryEmbedding);
        var source = CorpusSources.Normalize(corpusSource);

        var chunks = _db.DocumentChunks
            .Include(c => c.Document)
            .Where(c => c.Embedding != null);

        chunks = source switch
        {
            CorpusSources.Local => chunks.Where(c => c.Document!.GraphDriveId == CorpusSources.LocalDriveId),
            CorpusSources.Online => chunks.Where(c => CorpusSources.IsOnlineDocument(c.Document!)),
            CorpusSources.Planner => chunks.Where(c => c.Document!.GraphDriveId == CorpusSources.PlannerDriveId),
            CorpusSources.Battlecards => chunks.Where(c => c.Document!.GraphDriveId == CorpusSources.BattlecardsDriveId),
            _ => chunks
        };

        // Over-fetch before ACL filtering so a few restricted hits don't leave the caller
        // with fewer than topK usable results.
        var candidates = await chunks
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(topK * 3)
            .Select(c => new
            {
                Chunk = c,
                Distance = c.Embedding!.CosineDistance(queryVector)
            })
            .ToListAsync(ct);

        var accessibleIds = await _accessControl.FilterAccessibleDocumentIdsAsync(
            userObjectId, candidates.Select(c => c.Chunk.DocumentId).Distinct(), ct);

        var boostRows = await _db.DocumentWinBoosts.AsNoTracking()
            .Where(b => accessibleIds.Contains(b.DocumentId))
            .ToListAsync(ct);
        var boostByDoc = boostRows
            .GroupBy(b => b.DocumentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Boost));

        return candidates
            .Where(c => accessibleIds.Contains(c.Chunk.DocumentId))
            .Select(c =>
            {
                var baseScore = 1 - c.Distance;
                boostByDoc.TryGetValue(c.Chunk.DocumentId, out var boost);
                return new
                {
                    c.Chunk,
                    Score = Math.Min(1.0, baseScore + boost)
                };
            })
            .OrderByDescending(c => c.Score)
            .Take(topK)
            .Select(c => new SearchResultItem
            {
                Source = new Citation
                {
                    DocumentId = c.Chunk.DocumentId,
                    FileName = c.Chunk.Document!.FileName,
                    Locator = c.Chunk.Locator,
                    SharePointUrl = c.Chunk.Document.SharePointUrl,
                    Snippet = Truncate(c.Chunk.Content, 220)
                },
                Excerpt = Truncate(c.Chunk.Content, 400),
                Score = Math.Round(c.Score, 4)
            })
            .ToList();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
