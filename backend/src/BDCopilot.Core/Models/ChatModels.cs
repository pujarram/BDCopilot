namespace BDCopilot.Core.Models;

public class ChatRequest
{
    public required string Message { get; set; }

    /// <summary>Optional prior turns, oldest first, for follow-up questions.</summary>
    public List<ChatTurn> History { get; set; } = new();

    /// <summary>Entra ID object id of the calling user — required for ACL-filtered retrieval.</summary>
    public required string UserObjectId { get; set; }
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
