using System.Diagnostics;
using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BDCopilot.Infrastructure.Services;

public class AiChatService : IAiChatService
{
    private const string SystemPrompt =
        """
        You are BD Copilot, an assistant for a business-development team. Answer only using the
        SOURCES provided below the question — if the sources don't contain the answer, say so
        plainly rather than guessing. Always be specific about which source backs each claim
        (the caller will render citation chips from the same source list, so do not invent files
        that are not present in SOURCES).
        """;

    private readonly ISemanticKernelFactory _kernelFactory;
    private readonly IVectorSearchService _vectorSearch;
    private readonly IChannelLiveDocumentService _channelLive;
    private readonly ITokenUsageTracker _tokenUsage;
    private readonly AiSettings _settings;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(
        ISemanticKernelFactory kernelFactory,
        IVectorSearchService vectorSearch,
        IChannelLiveDocumentService channelLive,
        ITokenUsageTracker tokenUsage,
        IOptions<AiSettings> settings,
        ILogger<AiChatService> logger)
    {
        _kernelFactory = kernelFactory;
        _vectorSearch = vectorSearch;
        _channelLive = channelLive;
        _tokenUsage = tokenUsage;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct = default)
    {
        List<SearchResultItem> hits;
        try
        {
            hits = await _vectorSearch.SearchAsync(request.Message, request.UserObjectId, topK: 6, ct: ct);
        }
        catch (Exception ex)
        {
            // Retrieval failures should not hard-fail chat — answer without citations.
            _logger.LogWarning(ex, "Vector search failed for chat; continuing without sources.");
            hits = [];
        }

        if (request.IncludeChannelLiveSearch)
        {
            try
            {
                var live = await _channelLive.SearchLiveChannelAsync(
                    request.Message, request.UserObjectId, ct);
                hits = MergeHits(hits, live);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live channel search failed; using indexed corpus only.");
            }
        }

        if (hits.Count == 0 && string.IsNullOrWhiteSpace(request.ExtraContext))
        {
            var model = _settings.Provider == "AzureOpenAI"
                ? _settings.AzureOpenAI.ChatDeployment
                : _settings.Ollama.ChatModel;
            return new ChatResponse
            {
                Answer =
                    "I couldn't find any indexed documents matching your question in the corpus I can access. " +
                    "Try rephrasing, pick a broader phrase, or confirm SharePoint/local sync has indexed the relevant " +
                    "DOCX, PDF, PPTX, or XLSX files (Document Library → sync health).",
                Citations = [],
                AiProvider = _settings.Provider,
                Model = model
            };
        }

        var system = string.IsNullOrWhiteSpace(request.ExtraContext)
            ? SystemPrompt
            : SystemPrompt + """

                When PLANNER_SNAPSHOT is present in the user message, prefer those live Planner facts
                for schedule, assignees, delays, and health. Use SharePoint SOURCES for related documents.
                """;

        return await GroundedAnswerAsync(
            system, request.Message, hits, request.History, request.ExtraContext,
            "Chat", request.UserObjectId, ct);
    }

    public async Task<KnowledgeSearchResponse> SearchAndAnswerAsync(
        KnowledgeSearchRequest request,
        CancellationToken ct = default)
    {
        const string searchSystemPrompt =
            """
            You are BD Copilot Knowledge Search. Given SOURCES from the team's document library,
            write a concise answer (3–6 sentences) that a business-development person can reuse.
            Prefer concrete phrasing from the sources (security, ROI, delivery, commercials when present).
            If sources are empty or irrelevant, say you found no usable material and suggest refining the query.
            Do not invent file names — citations are rendered separately from the SOURCES list.
            """;

        List<SearchResultItem> hits;
        try
        {
            hits = await _vectorSearch.SearchAsync(
                request.Query,
                request.UserObjectId,
                topK: request.TopK <= 0 ? 8 : request.TopK,
                corpusSource: request.CorpusSource,
                ct: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector search failed for knowledge search.");
            hits = [];
        }

        if (hits.Count == 0)
        {
            return new KnowledgeSearchResponse
            {
                Query = request.Query,
                Answer = "No accessible documents matched that query in the selected corpus. Try Local vs Online, a broader phrase, or index more documents.",
                Citations = [],
                Results = [],
                AiProvider = _settings.Provider,
                Model = _settings.Provider == "AzureOpenAI"
                    ? _settings.AzureOpenAI.ChatDeployment
                    : _settings.Ollama.ChatModel
            };
        }

        var chat = await GroundedAnswerAsync(
            searchSystemPrompt, request.Query, hits, history: null, extraContext: null,
            "Search", request.UserObjectId, ct);

        return new KnowledgeSearchResponse
        {
            Query = request.Query,
            Answer = chat.Answer,
            Citations = chat.Citations,
            Results = hits,
            AiProvider = chat.AiProvider,
            Model = chat.Model
        };
    }

    private async Task<ChatResponse> GroundedAnswerAsync(
        string systemPrompt,
        string question,
        List<SearchResultItem> hits,
        List<ChatTurn>? history,
        string? extraContext,
        string operation,
        string userObjectId,
        CancellationToken ct)
    {
        var kernel = _kernelFactory.CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var model = _settings.Provider == "AzureOpenAI"
            ? _settings.AzureOpenAI.ChatDeployment
            : _settings.Ollama.ChatModel;

        var chatHistory = new ChatHistory(systemPrompt);
        foreach (var turn in history ?? [])
        {
            if (string.IsNullOrWhiteSpace(turn.Content)) continue;
            if (turn.Role == "assistant") chatHistory.AddAssistantMessage(turn.Content);
            else chatHistory.AddUserMessage(turn.Content);
        }

        chatHistory.AddUserMessage(BuildGroundedPrompt(question, hits, extraContext));

        try
        {
            var sw = Stopwatch.StartNew();
            var reply = await chat.GetChatMessageContentAsync(chatHistory, kernel: kernel, cancellationToken: ct);
            sw.Stop();

            var (promptTokens, completionTokens) = ExtractUsage(reply);
            try
            {
                await _tokenUsage.TrackAsync(new TokenUsageRecord
                {
                    UserObjectId = string.IsNullOrWhiteSpace(userObjectId) ? "anonymous" : userObjectId,
                    Operation = operation,
                    Provider = _settings.Provider,
                    Model = model,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = promptTokens + completionTokens,
                    DurationMs = (int)Math.Min(sw.ElapsedMilliseconds, int.MaxValue)
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Token usage tracking failed (non-fatal).");
            }

            return new ChatResponse
            {
                Answer = reply.Content ?? string.Empty,
                Citations = DedupeCitations(hits),
                AiProvider = _settings.Provider,
                Model = model
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"AI provider unreachable or timed out (provider={_settings.Provider}, model={model}). " +
                "Confirm Ollama is running at Ai:Ollama:Endpoint and the chat model is pulled.",
                ex);
        }
    }

    private static (int Prompt, int Completion) ExtractUsage(ChatMessageContent reply)
    {
        try
        {
            if (reply.Metadata is null) return (0, 0);
            if (reply.Metadata.TryGetValue("Usage", out var usageObj) && usageObj is not null)
            {
                var type = usageObj.GetType();
                var prompt = type.GetProperty("InputTokenCount")?.GetValue(usageObj)
                             ?? type.GetProperty("PromptTokens")?.GetValue(usageObj);
                var completion = type.GetProperty("OutputTokenCount")?.GetValue(usageObj)
                                 ?? type.GetProperty("CompletionTokens")?.GetValue(usageObj);
                return (ToInt(prompt), ToInt(completion));
            }
        }
        catch
        {
            // ignore
        }

        return (0, 0);
    }

    private static int ToInt(object? value) =>
        value switch
        {
            int i => i,
            long l => (int)Math.Min(l, int.MaxValue),
            _ => 0
        };

    private static string BuildGroundedPrompt(
        string question,
        IEnumerable<SearchResultItem> hits,
        string? extraContext = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"QUESTION: {question}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(extraContext))
        {
            sb.AppendLine("PLANNER_SNAPSHOT:");
            sb.AppendLine(extraContext.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("SOURCES:");
        var i = 1;
        foreach (var hit in hits)
        {
            var locator = string.IsNullOrWhiteSpace(hit.Source.Locator) ? "" : $" · {hit.Source.Locator}";
            sb.AppendLine($"[{i}] {hit.Source.FileName}{locator}");
            sb.AppendLine(hit.Excerpt);
            sb.AppendLine();
            i++;
        }
        return sb.ToString();
    }

    private static List<Citation> DedupeCitations(IEnumerable<SearchResultItem> hits) =>
        hits.Select(h => h.Source)
            .GroupBy(c => $"{c.FileName}|{c.Locator ?? ""}|{c.SharePointUrl}")
            .Select(g => g.First())
            .ToList();

    private static List<SearchResultItem> MergeHits(
        IReadOnlyList<SearchResultItem> indexed,
        IReadOnlyList<SearchResultItem> live)
    {
        if (live.Count == 0)
        {
            return indexed.ToList();
        }

        var merged = indexed.ToList();
        var seen = new HashSet<string>(
            merged.Select(h => $"{h.Source.FileName}|{h.Source.SharePointUrl}"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var hit in live)
        {
            var key = $"{hit.Source.FileName}|{hit.Source.SharePointUrl}";
            if (seen.Add(key))
            {
                merged.Add(hit);
            }
        }

        return merged.OrderByDescending(h => h.Score).Take(8).ToList();
    }
}
