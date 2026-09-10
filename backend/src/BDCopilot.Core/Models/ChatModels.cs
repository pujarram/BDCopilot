namespace BDCopilot.Core.Models;

public class ChatRequest
{
    public required string Message { get; set; }

    /// <summary>Optional prior turns, oldest first, for follow-up questions.</summary>
    public List<ChatTurn> History { get; set; } = new();

    /// <summary>Entra ID object id of the calling user — required for ACL-filtered retrieval.</summary>
    public required string UserObjectId { get; set; }

    /// <summary>
    /// Optional Planner / project snapshot injected into the grounded prompt
    /// (Teams bot Project Intelligence NL).
    /// </summary>
    public string? ExtraContext { get; set; }

    /// <summary>
    /// When true (Teams bot), also search the configured channel document library live via Graph
    /// in addition to the indexed corpus.
    /// </summary>
    public bool IncludeChannelLiveSearch { get; set; }
}

public class ChatTurn
{
    public required string Role { get; set; } // "user" | "assistant"
    public required string Content { get; set; }
}

public class ChatResponse
{
    public required string Answer { get; set; }

    public List<Citation> Citations { get; set; } = new();

    public required string AiProvider { get; set; } // "Ollama" | "AzureOpenAI"

    public required string Model { get; set; }
}
