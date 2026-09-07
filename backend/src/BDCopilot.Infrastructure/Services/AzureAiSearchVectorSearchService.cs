using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Optional Azure AI Search vector backend — used when <c>AzureSearch:Endpoint</c> is configured.
/// Falls back to <see cref="VectorSearchService"/> (Postgres/pgvector) otherwise.
/// Supports tenant + corpus filters for multi-tenant scale-out.
/// </summary>
public class AzureAiSearchVectorSearchService : IVectorSearchService
{
    private readonly AzureAiSearchSettings _settings;
    private readonly GraphSyncSettings _graph;
    private readonly IAccessControlService _accessControl;
    private readonly IEmbeddingService _embeddings;
    private readonly ILogger<AzureAiSearchVectorSearchService> _logger;

    public AzureAiSearchVectorSearchService(
        IOptions<AzureAiSearchSettings> settings,
        IOptions<GraphSyncSettings> graph,
        IAccessControlService accessControl,
        IEmbeddingService embeddings,
        ILogger<AzureAiSearchVectorSearchService> logger)
    {
        _settings = settings.Value;
        _graph = graph.Value;
        _accessControl = accessControl;
        _embeddings = embeddings;
        _logger = logger;
    }

    public async Task<List<SearchResultItem>> SearchAsync(
        string query,
        string userObjectId,
        int topK = 8,
        string? corpusSource = null,
        CancellationToken ct = default)
    {
        var corpus = CorpusSources.Normalize(corpusSource);
        if (corpus is CorpusSources.Local or CorpusSources.Planner)
        {
            _logger.LogDebug(
                "{Corpus} corpus requested — Azure AI Search index holds Online chunks; use Postgres for Local/Planner.",
                corpus);
            return [];
        }

        if (string.IsNullOrWhiteSpace(_settings.Endpoint))
        {
            _logger.LogWarning("Azure AI Search endpoint not configured — returning empty results.");
            return [];
        }

        var credential = string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? new AzureKeyCredential(string.Empty)
            : new AzureKeyCredential(_settings.ApiKey);

        var client = new SearchClient(new Uri(_settings.Endpoint), _settings.IndexName, credential);
        var queryEmbedding = await _embeddings.EmbedAsync(query, ct);

        var vectorQuery = new VectorizedQuery(queryEmbedding)
        {
            KNearestNeighborsCount = topK * 3,
            Fields = { "embedding" }
        };

        var options = new SearchOptions
        {
            Size = topK * 3,
            Select = { "documentId", "fileName", "sharePointUrl", "locator", "content", _settings.TenantIdField, _settings.CorpusSourceField }
        };
        options.VectorSearch = new VectorSearchOptions();
        options.VectorSearch.Queries.Add(vectorQuery);

        var filter = BuildFilter(corpus);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            options.Filter = filter;
        }

        var response = await client.SearchAsync<SearchDocument>("*", options, ct);
        var candidates = new List<(SearchDocument Doc, double Score)>();

        await foreach (var result in response.Value.GetResultsAsync())
        {
            candidates.Add((result.Document, result.Score ?? 0));
        }

        var documentIds = candidates
            .Select(c => Guid.TryParse(c.Doc.GetString("documentId"), out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct();

        var accessibleIds = await _accessControl.FilterAccessibleDocumentIdsAsync(userObjectId, documentIds, ct);

        return candidates
            .Where(c => Guid.TryParse(c.Doc.GetString("documentId"), out var id) && accessibleIds.Contains(id))
            .Take(topK)
            .Select(c => new SearchResultItem
            {
                Source = new Citation
                {
                    DocumentId = Guid.Parse(c.Doc.GetString("documentId")!),
                    FileName = c.Doc.GetString("fileName") ?? "",
                    Locator = c.Doc.TryGetValue("locator", out var loc) ? loc?.ToString() : null,
                    SharePointUrl = c.Doc.GetString("sharePointUrl") ?? "",
                    Snippet = Truncate(c.Doc.GetString("content") ?? "", 220)
                },
                Excerpt = Truncate(c.Doc.GetString("content") ?? "", 400),
                Score = Math.Round(c.Score, 4)
            })
            .ToList();
    }

    private string? BuildFilter(string corpus)
    {
        var clauses = new List<string>();

        if (_settings.EnableTenantFilter)
        {
            var tenantId = !string.IsNullOrWhiteSpace(_settings.DefaultTenantId)
                ? _settings.DefaultTenantId
                : _graph.TenantId;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                clauses.Add($"{_settings.TenantIdField} eq '{EscapeOData(tenantId)}'");
            }
        }

        if (corpus == CorpusSources.Online)
        {
            clauses.Add($"{_settings.CorpusSourceField} eq '{CorpusSources.Online}'");
        }

        return clauses.Count == 0 ? null : string.Join(" and ", clauses);
    }

    private static string EscapeOData(string value) => value.Replace("'", "''");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
