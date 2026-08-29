using Pgvector;

namespace BDCopilot.Core.Models;

/// <summary>
/// One retrievable slice of a document: the text, its embedding, and enough locator
/// information (page / slide / section) to render a citation chip like "Proposal_A.pdf · p.12".
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DocumentId { get; set; }

    public Document? Document { get; set; }

    public int ChunkIndex { get; set; }

    public required string Content { get; set; }

    /// <summary>e.g. "Page 12", "Slide 18", "Section 5" — whatever the parser could resolve.</summary>
    public string? Locator { get; set; }

    /// <summary>
    /// The chunk's embedding. Dimension must match <c>Ai:EmbeddingDimensions</c> in configuration
    /// for the lifetime of this index — switching the embedding model (e.g. Ollama's
    /// nomic-embed-text at 768 dims vs Azure OpenAI's text-embedding-3-large) requires a full
    /// re-index, not just a config change. See README "Switching embedding models".
    /// </summary>
    public Vector? Embedding { get; set; }
}
