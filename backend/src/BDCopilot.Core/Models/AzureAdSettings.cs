namespace BDCopilot.Core.Models;

public class AzureAdSettings
{
    public const string SectionName = "AzureAd";

    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = "";

    /// <summary>API app registration client id (JWT audience).</summary>
    public string ClientId { get; set; } = "";

    /// <summary>SPA / Angular app registration client id for MSAL. Falls back to ClientId when empty.</summary>
    public string SpaClientId { get; set; } = "";

    public string Audience { get; set; } = "";

    /// <summary>Scope the SPA requests, e.g. api://{ClientId}/access_as_user.</summary>
    public string ApiScope { get; set; } = "";

    /// <summary>
    /// When true, every document access is verified live against Microsoft Graph permissions.
    /// When false (typical local dev), Graph:AllowDevBypass applies.
    /// </summary>
    public bool EnforceAcl { get; set; }

    /// <summary>
    /// When true and Entra is configured, all API endpoints require an authenticated user.
    /// Keep false during local Angular ↔ API demos without SSO.
    /// </summary>
    public bool RequireAuthOnApi { get; set; }

    /// <summary>
    /// When true, POST /api/auth/login (admin/password) remains available alongside Entra SSO.
    /// Set false in production after MSAL cutover.
    /// </summary>
    public bool AllowPilotAdminLogin { get; set; } = true;
}
