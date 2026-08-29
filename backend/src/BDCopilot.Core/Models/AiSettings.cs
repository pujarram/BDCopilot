namespace BDCopilot.Core.Models;

/// <summary>
/// Bound from the "Ai" section of appsettings.{Environment}.json. This is the one setting that
/// changes between a developer's laptop and production — everything downstream of
/// ISemanticKernelFactory talks to a single Semantic Kernel Kernel instance and never knows
/// which provider is behind it.
/// </summary>
public class AiSettings
{
    public const string SectionName = "Ai";

    /// <summary>"Ollama" or "AzureOpenAI".</summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>
    /// Embedding vector width. Must stay constant for the lifetime of the pgvector index —
    /// see DocumentChunk.Embedding. nomic-embed-text (Ollama) = 768, text-embedding-3-large
    /// (Azure OpenAI) defaults to 3072 but can be truncated via the "dimensions" request
    /// parameter; this app requests that truncation so both providers can share one schema.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 768;

    public OllamaSettings Ollama { get; set; } = new();

    public AzureOpenAiSettings AzureOpenAI { get; set; } = new();
}

public class OllamaSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "llama3.1:8b";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
}

public class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = "";
    public string ChatDeployment { get; set; } = "gpt-4o";
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";

    /// <summary>
    /// Prefer Entra ID (DefaultAzureCredential / managed identity) over a static key in
    /// production. ApiKey is provided only as a local-testing fallback and should come from
    /// Key Vault or user-secrets, never source control.
    /// </summary>
    public string? ApiKey { get; set; }

    public bool UseEntraIdAuth { get; set; } = true;
}
