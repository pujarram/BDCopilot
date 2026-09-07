using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

/// <summary>
/// Retrieval-augmented chat: embeds the question, searches the ACL-filtered vector store,
/// and asks the configured LLM (Ollama or Azure OpenAI, chosen by <c>Ai:Provider</c>) to answer
/// grounded only in what came back.
/// </summary>
public interface IAiChatService
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// Knowledge Search: ACL-trimmed retrieval plus a short grounded answer with citations.
    /// </summary>
    Task<KnowledgeSearchResponse> SearchAndAnswerAsync(KnowledgeSearchRequest request, CancellationToken ct = default);
}

/// <summary>Generates a full draft document (RFP, business case, proposal) from retrieved chunks.</summary>
public interface IDocumentGeneratorService
{
    Task<GeneratedDocument> GenerateRfpAsync(RfpGenerationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streams RFP generation live: outline → per-section tokens from the LLM → complete document.
    /// </summary>
    IAsyncEnumerable<RfpStreamEvent> GenerateRfpStreamAsync(RfpGenerationRequest request, CancellationToken ct = default);

    Task<GeneratedDocument> GenerateBusinessCaseAsync(BusinessCaseGenerationRequest request, CancellationToken ct = default);

    IAsyncEnumerable<RfpStreamEvent> GenerateBusinessCaseStreamAsync(BusinessCaseGenerationRequest request, CancellationToken ct = default);

    Task<GeneratedDocument> GenerateProposalAsync(ProposalGenerationRequest request, CancellationToken ct = default);

    IAsyncEnumerable<RfpStreamEvent> GenerateProposalStreamAsync(ProposalGenerationRequest request, CancellationToken ct = default);

    Task<GeneratedDocument> GenerateCompetitivePositioningAsync(
        CompetitivePositioningRequest request,
        CancellationToken ct = default);

    IAsyncEnumerable<RfpStreamEvent> GenerateCompetitivePositioningStreamAsync(
        CompetitivePositioningRequest request,
        CancellationToken ct = default);
}

/// <summary>Builds Word, PowerPoint, and ZIP exports from a generated document draft.</summary>
public interface IDocumentExportService
{
    Task<Stream> ExportDocxAsync(GeneratedDocument document, CancellationToken ct = default);

    Task<Stream> ExportPptxAsync(GeneratedDocument document, CancellationToken ct = default);

    Task<Stream> ExportZipAsync(GeneratedDocument document, CancellationToken ct = default);
}

/// <summary>Approve / feedback / export workflow for generated documents (Phase 3).</summary>
public interface IGenerationWorkflowService
{
    Task<GeneratedDocument> ApproveAsync(ApproveGenerationRequest request, CancellationToken ct = default);

    Task LogFeedbackAsync(GenerationFeedbackRequest request, CancellationToken ct = default);

    Task<ExportResult> ExportAsync(ExportGenerationRequest request, CancellationToken ct = default);
}
