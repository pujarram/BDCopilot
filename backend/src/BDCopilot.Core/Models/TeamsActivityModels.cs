namespace BDCopilot.Core.Models;

/// <summary>Minimal Bot Framework activity payload used by the Teams messaging endpoint.</summary>
public class TeamsActivity
{
    public string? Type { get; set; }
    public string? Id { get; set; }
    public string? Timestamp { get; set; }
    public string? ServiceUrl { get; set; }
    public string? ChannelId { get; set; }
    public string? Text { get; set; }
    public TeamsChannelAccount? From { get; set; }
    public TeamsChannelAccount? Recipient { get; set; }
    public TeamsConversationAccount? Conversation { get; set; }
}

public class TeamsChannelAccount
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? AadObjectId { get; set; }
}

public class TeamsConversationAccount
{
    public string? Id { get; set; }
    public bool? IsGroup { get; set; }
    public string? ConversationType { get; set; }
    public string? TenantId { get; set; }
}

public class TeamsReply
{
    public required string Text { get; set; }
}
