using System.Runtime.CompilerServices;
using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Drafts RFP responses, business cases and proposals from retrieved chunks. Every method
/// returns a <see cref="GeneratedDocument"/> with Status = "Draft" — nothing here writes a
/// .docx/.pptx file yet. Wiring OpenXML export and the human-approval step is Phase 3 work
/// (see roadmap); this service is deliberately export-agnostic so that step can be added
/// without touching generation logic.
/// </summary>
public class DocumentGeneratorService : IDocumentGeneratorService
{
    private static readonly string[] RfpSectionTitles =
    {
        "Executive Summary", "Understanding of Requirements", "Proposed Solution & Architecture",
        "Security & Compliance", "Delivery Timeline & Team", "Commercials & Pricing"
    };

    private static readonly string[] BusinessCaseSectionTitles =
    {
        "Executive Summary", "Benefits & ROI", "Risks & Mitigations", "Recommendation & Next Steps"
    };

    private static readonly string[] ProposalSectionTitles =
    {
        "Executive Summary", "Approach & Solution", "Architecture & Delivery", "Commercials & Pricing"
    };

    private readonly ISemanticKernelFactory _kernelFactory;
    private readonly IVectorSearchService _vectorSearch;

    public DocumentGeneratorService(ISemanticKernelFactory kernelFactory, IVectorSearchService vectorSearch)
    {
        _kernelFactory = kernelFactory;
        _vectorSearch = vectorSearch;
    }

    public async Task<GeneratedDocument> GenerateRfpAsync(RfpGenerationRequest request, CancellationToken ct = default)
    {
        GeneratedDocument? document = null;
        await foreach (var evt in GenerateRfpStreamAsync(request, ct))
        {
            if (evt.Type == "complete" && evt.Document is not null)
            {
                document = evt.Document;
            }
        }

        return document ?? new GeneratedDocument
        {
            Title = $"{request.Title} — RFP Response",
            Sections = []
        };
    }

    public async IAsyncEnumerable<RfpStreamEvent> GenerateRfpStreamAsync(
        RfpGenerationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generationId = Guid.NewGuid();
        var documentTitle = $"{request.Title} — RFP Response";

        yield return new RfpStreamEvent
        {
            Type = "outline",
            GenerationId = generationId,
            DocumentTitle = documentTitle,
            SectionTitles = RfpSectionTitles.ToList()
        };

        List<SearchResultItem>? hits = null;
        string? retrievalError = null;
        try
        {
            hits = await _vectorSearch.SearchAsync(
                BuildRetrievalQuery($"{request.Title} for {request.Customer}", request.FocusNotes),
                request.UserObjectId,
                topK: 12,
                corpusSource: request.CorpusSource,
                ct: ct);
        }
        catch (Exception ex)
        {
            retrievalError = $"Retrieval failed: {ex.Message}";
        }

        if (retrievalError is not null)
        {
            yield return new RfpStreamEvent
            {
                Type = "error",
                GenerationId = generationId,
                Message = retrievalError
            };
            yield break;
        }

        var sections = new List<GeneratedSection>();
        var order = 1;

        foreach (var title in RfpSectionTitles)
        {
            ct.ThrowIfCancellationRequested();

            var relevant = hits!.Take(3).ToList();
            yield return new RfpStreamEvent
            {
                Type = "section-start",
                GenerationId = generationId,
                Order = order,
                Title = title,
                Sources = relevant.Select(h => h.Source).ToList()
            };

            var instruction =
                $"Write the \"{title}\" section of an RFP response titled \"{request.Title}\" for {request.Customer}. " +
                $"Tone: {request.Tone}. Base it only on the SOURCES." +
                FocusInstruction(request.FocusNotes);

            var contentBuilder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(instruction, relevant, ct))
            {
                if (string.IsNullOrEmpty(delta))
                {
                    continue;
                }

                contentBuilder.Append(delta);
                yield return new RfpStreamEvent
                {
                    Type = "token",
                    GenerationId = generationId,
                    Order = order,
                    Title = title,
                    Delta = delta
                };
            }

            var content = contentBuilder.ToString();
            var section = new GeneratedSection
            {
                Order = order,
                Title = title,
                Content = content,
                Sources = relevant.Select(h => h.Source).ToList()
            };
            sections.Add(section);

            yield return new RfpStreamEvent
            {
                Type = "section-done",
                GenerationId = generationId,
                Order = order,
                Title = title,
                Content = content,
                Sources = section.Sources
            };

            order++;
        }

        yield return new RfpStreamEvent
        {
            Type = "complete",
            GenerationId = generationId,
            DocumentTitle = documentTitle,
            Document = new GeneratedDocument
            {
                GenerationId = generationId,
                Title = documentTitle,
                Sections = sections,
                Status = "Draft",
                CreatedAt = DateTimeOffset.UtcNow
            }
        };
    }

    public async Task<GeneratedDocument> GenerateBusinessCaseAsync(BusinessCaseGenerationRequest request, CancellationToken ct = default)
    {
        GeneratedDocument? document = null;
        await foreach (var evt in GenerateBusinessCaseStreamAsync(request, ct))
        {
            if (evt.Type == "complete" && evt.Document is not null)
            {
                document = evt.Document;
            }
        }

        return document ?? new GeneratedDocument
        {
            Title = $"Business Case — {request.Initiative}",
            Sections = []
        };
    }

    public async IAsyncEnumerable<RfpStreamEvent> GenerateBusinessCaseStreamAsync(
        BusinessCaseGenerationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generationId = Guid.NewGuid();
        var documentTitle = $"Business Case — {request.Initiative}";
        var hits = await _vectorSearch.SearchAsync(
            BuildRetrievalQuery($"business case, benefits and ROI for {request.Initiative}", request.FocusNotes),
            request.UserObjectId,
            topK: 10,
            corpusSource: request.CorpusSource,
            ct: ct);

        yield return new RfpStreamEvent
        {
            Type = "outline",
            GenerationId = generationId,
            DocumentTitle = documentTitle,
            SectionTitles = BusinessCaseSectionTitles.ToList()
        };

        var sections = new List<GeneratedSection>();
        for (var i = 0; i < BusinessCaseSectionTitles.Length; i++)
        {
            var title = BusinessCaseSectionTitles[i];
            var order = i + 1;
            yield return new RfpStreamEvent
            {
                Type = "section-start",
                GenerationId = generationId,
                Order = order,
                Title = title
            };

            var instruction =
                $"Write the \"{title}\" section of a business case for initiative \"{request.Initiative}\", " +
                $"pitched at a {request.Audience}. Base it only on the SOURCES. Be concrete about benefits, " +
                "ROI ranges, risks, and next steps when the section calls for them." +
                FocusInstruction(request.FocusNotes);

            var builder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(instruction, hits, ct))
            {
                builder.Append(delta);
                yield return new RfpStreamEvent
                {
                    Type = "token",
                    GenerationId = generationId,
                    Order = order,
                    Title = title,
                    Delta = delta
                };
            }

            var section = new GeneratedSection
            {
                Order = order,
                Title = title,
                Content = builder.ToString(),
                Sources = hits.Select(h => h.Source).ToList()
            };
            sections.Add(section);

            yield return new RfpStreamEvent
            {
                Type = "section-done",
                GenerationId = generationId,
                Order = order,
                Title = title,
                Content = section.Content,
                Sources = section.Sources
            };
        }

        yield return new RfpStreamEvent
        {
            Type = "complete",
            GenerationId = generationId,
            Document = new GeneratedDocument
            {
                GenerationId = generationId,
                Title = documentTitle,
                Sections = sections,
                Status = "Draft"
            }
        };
    }

    public async Task<GeneratedDocument> GenerateProposalAsync(ProposalGenerationRequest request, CancellationToken ct = default)
    {
        GeneratedDocument? document = null;
        await foreach (var evt in GenerateProposalStreamAsync(request, ct))
        {
            if (evt.Type == "complete" && evt.Document is not null)
            {
                document = evt.Document;
            }
        }

        return document ?? new GeneratedDocument
        {
            Title = $"Proposal — {request.Solution}",
            Sections = []
        };
    }

    public async IAsyncEnumerable<RfpStreamEvent> GenerateProposalStreamAsync(
        ProposalGenerationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generationId = Guid.NewGuid();
        var documentTitle = $"Proposal — {request.Solution}";
        var hits = await _vectorSearch.SearchAsync(
            request.Solution,
            request.UserObjectId,
            topK: 12,
            corpusSource: request.CorpusSource,
            ct: ct);

        yield return new RfpStreamEvent
        {
            Type = "outline",
            GenerationId = generationId,
            DocumentTitle = documentTitle,
            SectionTitles = ProposalSectionTitles.ToList()
        };

        var sections = new List<GeneratedSection>();
        for (var i = 0; i < ProposalSectionTitles.Length; i++)
        {
            var title = ProposalSectionTitles[i];
            var order = i + 1;
            yield return new RfpStreamEvent
            {
                Type = "section-start",
                GenerationId = generationId,
                Order = order,
                Title = title
            };

            var extras = new StringBuilder();
            if (request.IncludeDeck)
            {
                extras.Append(" Include talking points suitable for a slide deck. ");
            }

            if (request.IncludeArchitectureDiagram)
            {
                extras.Append(" Describe architecture components clearly enough to sketch a diagram. ");
            }

            var instruction =
                $"Write the \"{title}\" section of a proposal for \"{request.Solution}\". " +
                extras +
                "Base it only on the SOURCES.";

            var builder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(instruction, hits, ct))
            {
                builder.Append(delta);
                yield return new RfpStreamEvent
                {
                    Type = "token",
                    GenerationId = generationId,
                    Order = order,
                    Title = title,
                    Delta = delta
                };
            }

            var section = new GeneratedSection
            {
                Order = order,
                Title = title,
                Content = builder.ToString(),
                Sources = hits.Select(h => h.Source).ToList()
            };
            sections.Add(section);

            yield return new RfpStreamEvent
            {
                Type = "section-done",
                GenerationId = generationId,
                Order = order,
                Title = title,
                Content = section.Content,
                Sources = section.Sources
            };
        }

        yield return new RfpStreamEvent
        {
            Type = "complete",
            GenerationId = generationId,
            Document = new GeneratedDocument
            {
                GenerationId = generationId,
                Title = documentTitle,
                Sections = sections,
                Status = "Draft"
            }
        };
    }

    private async Task<string> DraftSectionAsync(string instruction, List<SearchResultItem> hits, CancellationToken ct)
    {
        var builder = new StringBuilder();
        await foreach (var delta in DraftSectionStreamAsync(instruction, hits, ct))
        {
            builder.Append(delta);
        }

        return builder.ToString();
    }

    private async IAsyncEnumerable<string> DraftSectionStreamAsync(
        string instruction,
        List<SearchResultItem> hits,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var kernel = _kernelFactory.CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory(
            "You draft business-development documents grounded strictly in the SOURCES you are given. " +
            "Never invent client names, metrics, or file names that are not present in SOURCES. " +
            "Prefer concrete reuse of prior proposal/business-case language when sources support it. " +
            "Flag any sentence that reuses wording from a source closely enough that it should be reviewed " +
            "before sending, by prefixing that sentence with [reused]. " +
            "If sources are thin, write a short honest draft and call out gaps instead of fabricating.");

        var sourcesBlock = string.Join("\n\n", hits.Select((h, i) => $"[{i + 1}] {h.Source.FileName}\n{h.Excerpt}"));
        history.AddUserMessage($"{instruction}\n\nSOURCES:\n{sourcesBlock}");

        await foreach (var message in chat.GetStreamingChatMessageContentsAsync(history, kernel: kernel, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(message.Content))
            {
                yield return message.Content;
            }
        }
    }

    private static string BuildRetrievalQuery(string baseQuery, string? focusNotes) =>
        string.IsNullOrWhiteSpace(focusNotes)
            ? baseQuery
            : $"{baseQuery}\n{focusNotes.Trim()}";

    private static string FocusInstruction(string? focusNotes) =>
        string.IsNullOrWhiteSpace(focusNotes)
            ? ""
            : " Prioritize reusing or aligning with this focus material from Knowledge Search when relevant:\n" +
              focusNotes.Trim();
}
