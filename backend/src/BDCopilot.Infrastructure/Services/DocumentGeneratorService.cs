using System.Runtime.CompilerServices;
using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Options;
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
    private readonly CompliancePackSettings _compliance;

    public DocumentGeneratorService(
        ISemanticKernelFactory kernelFactory,
        IVectorSearchService vectorSearch,
        IOptions<CompliancePackSettings> compliance)
    {
        _kernelFactory = kernelFactory;
        _vectorSearch = vectorSearch;
        _compliance = compliance.Value;
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
                FocusInstruction(request.FocusNotes) +
                GenerationContextSuffix(request.Language, request.ComplianceRegion);

            var contentBuilder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(instruction, relevant, request.Language, request.ComplianceRegion, ct))
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
                FocusInstruction(request.FocusNotes) +
                GenerationContextSuffix(request.Language, request.ComplianceRegion);

            var builder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(instruction, hits, request.Language, request.ComplianceRegion, ct))
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
                "Base it only on the SOURCES." +
                GenerationContextSuffix(request.Language, request.ComplianceRegion);

            var builder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(instruction, hits, request.Language, request.ComplianceRegion, ct))
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

    private static readonly string[] CompetitiveSectionTitles =
    {
        "Executive Positioning", "Where We Win", "Competitor Strengths to Acknowledge",
        "Proof Points & References", "Recommended Talk Track"
    };

    public async Task<GeneratedDocument> GenerateCompetitivePositioningAsync(
        CompetitivePositioningRequest request,
        CancellationToken ct = default)
    {
        GeneratedDocument? document = null;
        await foreach (var evt in GenerateCompetitivePositioningStreamAsync(request, ct))
        {
            if (evt.Type == "complete" && evt.Document is not null)
            {
                document = evt.Document;
            }
        }

        return document ?? new GeneratedDocument
        {
            Title = $"Competitive positioning vs {request.Competitor}",
            Sections = []
        };
    }

    public async IAsyncEnumerable<RfpStreamEvent> GenerateCompetitivePositioningStreamAsync(
        CompetitivePositioningRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var generationId = Guid.NewGuid();
        var documentTitle = $"Competitive positioning — {request.OurSolution} vs {request.Competitor}";
        var query =
            $"competitive positioning battlecard against {request.Competitor} for {request.OurSolution}. " +
            (request.CustomerContext ?? "");

        var hits = await _vectorSearch.SearchAsync(
            query,
            request.UserObjectId,
            topK: 10,
            corpusSource: CorpusSources.Battlecards,
            ct: ct);

        // Fall back to Online if battlecards corpus is empty.
        if (hits.Count == 0)
        {
            hits = await _vectorSearch.SearchAsync(
                query,
                request.UserObjectId,
                topK: 10,
                corpusSource: CorpusSources.Online,
                ct: ct);
        }

        yield return new RfpStreamEvent
        {
            Type = "outline",
            GenerationId = generationId,
            DocumentTitle = documentTitle,
            SectionTitles = CompetitiveSectionTitles.ToList()
        };

        var sections = new List<GeneratedSection>();
        for (var i = 0; i < CompetitiveSectionTitles.Length; i++)
        {
            var title = CompetitiveSectionTitles[i];
            var order = i + 1;
            yield return new RfpStreamEvent
            {
                Type = "section-start",
                GenerationId = generationId,
                Order = order,
                Title = title
            };

            var instruction =
                $"Write the \"{title}\" section of a competitive positioning brief for \"{request.OurSolution}\" " +
                $"against competitor \"{request.Competitor}\". " +
                (string.IsNullOrWhiteSpace(request.CustomerContext)
                    ? ""
                    : $"Customer context: {request.CustomerContext}. ") +
                "Ground claims only in SOURCES (battlecards). Be fair, factual, and sales-usable." +
                GenerationContextSuffix(request.Language, request.ComplianceRegion);

            var builder = new StringBuilder();
            await foreach (var delta in DraftSectionStreamAsync(
                               instruction, hits, request.Language, request.ComplianceRegion, ct))
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
            DocumentTitle = documentTitle,
            Document = new GeneratedDocument
            {
                GenerationId = generationId,
                Title = documentTitle,
                Sections = sections,
                Status = "Draft"
            }
        };
    }

    private async Task<string> DraftSectionAsync(
        string instruction,
        List<SearchResultItem> hits,
        string? language,
        string? complianceRegion,
        CancellationToken ct)
    {
        var builder = new StringBuilder();
        await foreach (var delta in DraftSectionStreamAsync(instruction, hits, language, complianceRegion, ct))
        {
            builder.Append(delta);
        }

        return builder.ToString();
    }

    private async IAsyncEnumerable<string> DraftSectionStreamAsync(
        string instruction,
        List<SearchResultItem> hits,
        string? language,
        string? complianceRegion,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var kernel = _kernelFactory.CreateKernel();
        var chat = kernel.GetRequiredService<IChatCompletionService>();
        var pack = ResolveCompliancePack(complianceRegion);
        var exportLanguage = string.IsNullOrWhiteSpace(language) ? pack.ExportLanguage : language!.Trim();

        var history = new ChatHistory(
            "You draft business-development documents grounded strictly in the SOURCES you are given. " +
            "Never invent client names, metrics, or file names that are not present in SOURCES. " +
            "Prefer concrete reuse of prior proposal/business-case language when sources support it. " +
            "Flag any sentence that reuses wording from a source closely enough that it should be reviewed " +
            "before sending, by prefixing that sentence with [reused]. " +
            "If sources are thin, write a short honest draft and call out gaps instead of fabricating. " +
            $"Write in {exportLanguage}. {pack.PromptSuffix}");

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

    private string GenerationContextSuffix(string? language, string? complianceRegion)
    {
        var pack = ResolveCompliancePack(complianceRegion);
        var lang = string.IsNullOrWhiteSpace(language) ? pack.ExportLanguage : language!.Trim();
        return $" Output language: {lang}. Compliance pack ({pack.Label}): {pack.PromptSuffix}";
    }

    private ComplianceRegionPack ResolveCompliancePack(string? regionKey)
    {
        var key = string.IsNullOrWhiteSpace(regionKey) ? _compliance.DefaultRegion : regionKey.Trim();
        if (_compliance.Regions.TryGetValue(key, out var pack))
        {
            return pack;
        }

        return _compliance.Regions.TryGetValue(_compliance.DefaultRegion, out var fallback)
            ? fallback
            : new ComplianceRegionPack { Label = key, ExportLanguage = "en-US" };
    }
}
