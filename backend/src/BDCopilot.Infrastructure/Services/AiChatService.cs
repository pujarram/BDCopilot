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
    private readonly AiSettings _settings;
    private readonly ILogger<AiChatService> _logger;

    public AiChatService(
        ISemanticKernelFactory kernelFactory,
        IVectorSearchService vectorSearch,
        IOptions<AiSettings> settings,
        ILogger<AiChatService> logger)
    {
        _kernelFactory = kernelFactory;
        _vectorSearch = vectorSearch;
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

        return await GroundedAnswerAsync(SystemPrompt, request.Message, hits, request.History, ct);
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

        var chat = await GroundedAnswerAsync(searchSystemPrompt, request.Query, hits, history: null, ct);

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
        CancellationToken ct)
    {
        var kernel = _kernelFactory.CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var chatHistory = new ChatHistory(systemPrompt);
        foreach (var turn in history ?? [])
        {
            if (string.IsNullOrWhiteSpace(turn.Content)) continue;
            if (turn.Role == "assistant") chatHistory.AddAssistantMessage(turn.Content);
            else chatHistory.AddUserMessage(turn.Content);
        }

        chatHistory.AddUserMessage(BuildGroundedPrompt(question, hits));

        try
        {
            var reply = await chat.GetChatMessageContentAsync(chatHistory, kernel: kernel, cancellationToken: ct);

            return new ChatResponse
            {
                Answer = reply.Content ?? string.Empty,
                Citations = hits.Select(h => h.Source).ToList(),
                AiProvider = _settings.Provider,
                Model = _settings.Provider == "AzureOpenAI"
                    ? _settings.AzureOpenAI.ChatDeployment
                    : _settings.Ollama.ChatModel
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            var model = _settings.Provider == "AzureOpenAI"
                ? _settings.AzureOpenAI.ChatDeployment
                : _settings.Ollama.ChatModel;
            throw new InvalidOperationException(
                $"AI provider unreachable or timed out (provider={_settings.Provider}, model={model}). " +
                "Confirm Ollama is running at Ai:Ollama:Endpoint and the chat model is pulled.",
                ex);
        }
    }

    private static string BuildGroundedPrompt(string question, IEnumerable<SearchResultItem> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"QUESTION: {question}");
        sb.AppendLine();
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
}
