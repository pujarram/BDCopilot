namespace BDCopilot.Core.Models;

/// <summary>Azure Bot / Teams channel settings (Bot Framework messaging endpoint).</summary>
public class TeamsBotSettings
{
    public const string SectionName = "TeamsBot";

    /// <summary>When false, /api/channels/teams/messages rejects requests.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Azure Bot / Entra Application (client) ID. Defaults to Graph:ClientId when empty.
    /// </summary>
    public string MicrosoftAppId { get; set; } = "";

    /// <summary>
    /// Client secret Value (not Secret ID). Prefer user-secrets / Key Vault.
    /// Falls back to Graph:ClientSecret when empty.
    /// </summary>
    public string MicrosoftAppPassword { get; set; } = "";

    /// <summary>Entra tenant id for single-tenant bot token. Falls back to Graph:TenantId.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>Optional shared secret for local testing without Bot Framework JWT.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>
    /// Public HTTPS base for the Angular tab app (ngrok to :4200), e.g. https://xxxx.ngrok-free.app
    /// Used in bot help replies and deep links.
    /// </summary>
    public string WebBaseUrl { get; set; } = "https://YOUR-NGROK-4200.ngrok-free.app";

    /// <summary>
    /// Public base URL for BDCopilot.Api (ngrok to :5154). Used for document open links in bot Sources.
    /// </summary>
    public string ApiBaseUrl { get; set; } = "http://localhost:5154";

    /// <summary>User object id used for RAG when Teams SSO is not yet wired.</summary>
    public string FallbackUserObjectId { get; set; } = "demo-user-0000-0000-0000-000000000000";
}
