using System.Text.RegularExpressions;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using GraphClientFactory = BDCopilot.Infrastructure.Graph.GraphClientFactory;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Reads recent channel library files live via Graph (not waiting for Hangfire sync).
/// </summary>
public sealed class ChannelLiveDocumentService : IChannelLiveDocumentService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "docx", "xlsx", "pptx", "pdf", "txt", "md"
    };

    private readonly GraphClientFactory _graphFactory;
    private readonly GraphSyncSettings _settings;
    private readonly IDocumentParserRouter _parserRouter;
    private readonly ILogger<ChannelLiveDocumentService> _logger;

    public ChannelLiveDocumentService(
        GraphClientFactory graphFactory,
        IOptions<GraphSyncSettings> settings,
        IDocumentParserRouter parserRouter,
        ILogger<ChannelLiveDocumentService> logger)
    {
        _graphFactory = graphFactory;
        _settings = settings.Value;
        _parserRouter = parserRouter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResultItem>> SearchLiveChannelAsync(
        string query,
        string userObjectId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(_settings.BdChannelDriveId))
        {
            return [];
        }

        var graph = _graphFactory.GetClient();
        if (graph is null)
        {
            return [];
        }

        try
        {
            var driveId = _settings.BdChannelDriveId.Trim();
            var folderPath = string.IsNullOrWhiteSpace(_settings.RfpFolderPath)
                ? "root"
                : _settings.RfpFolderPath.Trim().Trim('/');

            DriveItemCollectionResponse? children;
            if (string.Equals(folderPath, "root", StringComparison.OrdinalIgnoreCase))
            {
                children = await graph.Drives[driveId].Items["root"].Children
                    .GetAsync(cancellationToken: ct);
            }
            else
            {
                children = await graph.Drives[driveId].Root
                    .ItemWithPath(folderPath)
                    .Children
                    .GetAsync(cancellationToken: ct);
            }

            var files = (children?.Value ?? [])
                .Where(i => i.File is not null && !string.IsNullOrWhiteSpace(i.Name))
                .Where(i => SupportedExtensions.Contains(Path.GetExtension(i.Name!).TrimStart('.')))
                .OrderByDescending(i => i.LastModifiedDateTime)
                .Take(20)
                .ToList();

            if (files.Count == 0)
            {
                return [];
            }

            var terms = Tokenize(query);
            var results = new List<SearchResultItem>();

            foreach (var item in files)
            {
                if (results.Count >= 4)
                {
                    break;
                }

                var nameScore = ScoreName(item.Name!, terms);
                if (nameScore <= 0 && results.Count >= 2)
                {
                    continue;
                }

                try
                {
                    await using var stream = await graph.Drives[driveId].Items[item.Id!].Content
                        .GetAsync(cancellationToken: ct);
                    if (stream is null)
                    {
                        continue;
                    }

                    var parsed = await _parserRouter.ParseAsync(item.Name!, stream, ct);
                    var text = string.Join(' ', parsed.Sections.Select(s => s.Text));
                    var contentScore = ScoreContent(text, terms);
                    var score = Math.Max(nameScore, contentScore);
                    if (score <= 0)
                    {
                        continue;
                    }

                    var excerpt = parsed.Sections.Count > 0
                        ? parsed.Sections[0].Text
                        : text;
                    if (excerpt.Length > 400)
                    {
                        excerpt = excerpt[..400].TrimEnd() + "…";
                    }

                    var webUrl = item.WebUrl
                                 ?? $"https://graph.microsoft.com/v1.0/drives/{driveId}/items/{item.Id}";

                    results.Add(new SearchResultItem
                    {
                        Source = new Citation
                        {
                            DocumentId = Guid.Empty,
                            FileName = item.Name!,
                            Locator = "Channel (live)",
                            SharePointUrl = webUrl,
                            Snippet = Truncate(excerpt, 220)
                        },
                        Excerpt = excerpt,
                        Score = Math.Round(score, 4)
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Live channel read skipped for {Name}", item.Name);
                }
            }

            return results.OrderByDescending(r => r.Score).Take(4).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live channel document search failed.");
            return [];
        }
    }

    private static List<string> Tokenize(string query) =>
        Regex.Split(query.ToLowerInvariant(), @"\W+")
            .Where(t => t.Length > 2)
            .Distinct()
            .Take(8)
            .ToList();

    private static double ScoreName(string fileName, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return 0.35;
        }

        var lower = fileName.ToLowerInvariant();
        var hits = terms.Count(t => lower.Contains(t, StringComparison.Ordinal));
        return hits == 0 ? 0 : Math.Min(1.0, 0.4 + hits * 0.15);
    }

    private static double ScoreContent(string text, IReadOnlyList<string> terms)
    {
        if (string.IsNullOrWhiteSpace(text) || terms.Count == 0)
        {
            return 0;
        }

        var lower = text.ToLowerInvariant();
        var hits = terms.Count(t => lower.Contains(t, StringComparison.Ordinal));
        return hits == 0 ? 0 : Math.Min(1.0, 0.3 + hits * 0.12);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
