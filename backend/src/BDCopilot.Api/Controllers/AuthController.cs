using System.Text;
using System.Text.Json;
using BDCopilot.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BDCopilot.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly AdminUserSettings _settings;
    private readonly AzureAdSettings _azureAd;
    private readonly TeamsBotSettings _teamsBot;

    public AuthController(
        IOptions<AdminUserSettings> settings,
        IOptions<AzureAdSettings> azureAd,
        IOptions<TeamsBotSettings> teamsBot)
    {
        _settings = settings.Value;
        _azureAd = azureAd.Value;
        _teamsBot = teamsBot.Value;
    }

    /// <summary>Pilot admin login — disabled in production when AllowPilotAdminLogin is false.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AdminLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<AdminLoginResponse> Login([FromBody] AdminLoginRequest request)
    {
        if (!_azureAd.AllowPilotAdminLogin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                title = "Pilot admin login is disabled. Sign in with Microsoft Entra SSO."
            });
        }

        var users = _settings.Users.Count > 0
            ? _settings.Users
            : new List<AdminUserAccount>
            {
                new() { Username = "admin", Password = "admin123", DisplayName = "BD Admin", Role = "Admin" }
            };

        var match = users.FirstOrDefault(u =>
            string.Equals(u.Username, request.Username, StringComparison.OrdinalIgnoreCase) &&
            u.Password == request.Password);

        if (match is null)
        {
            return Unauthorized(new { title = "Invalid username or password." });
        }

        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var payload = JsonSerializer.Serialize(new
        {
            sub = match.Username,
            name = match.DisplayName,
            role = match.Role,
            exp = expires.ToUnixTimeSeconds()
        });
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

        return Ok(new AdminLoginResponse
        {
            Token = token,
            Username = match.Username,
            DisplayName = match.DisplayName,
            Role = match.Role,
            ExpiresAt = expires
        });
    }

    [HttpGet("config")]
    public ActionResult<object> Config()
    {
        var hasTenant = !string.IsNullOrWhiteSpace(_azureAd.TenantId);
        var hasApiClient = !string.IsNullOrWhiteSpace(_azureAd.ClientId);
        var hasSpaClient = !string.IsNullOrWhiteSpace(_azureAd.SpaClientId);
        var spaClientId = hasSpaClient ? _azureAd.SpaClientId : _azureAd.ClientId;
        var entraConfigured = hasTenant && !string.IsNullOrWhiteSpace(spaClientId);
        var spaFallsBackToApiClient = entraConfigured && !hasSpaClient;
        var apiScope = string.IsNullOrWhiteSpace(_azureAd.ApiScope)
            ? (hasApiClient ? $"api://{_azureAd.ClientId}/access_as_user" : null)
            : _azureAd.ApiScope;

        var setupHints = new List<string>();
        if (!entraConfigured)
        {
            setupHints.Add("Set AzureAd:TenantId and AzureAd:SpaClientId (SPA app registration) in appsettings or user-secrets.");
        }
        else
        {
            if (spaFallsBackToApiClient)
            {
                setupHints.Add(
                    "AzureAd:SpaClientId is empty — MSAL is using AzureAd:ClientId. " +
                    "Create a separate SPA app registration (platform: Single-page application) and set SpaClientId. " +
                    "Do not use the Azure Bot / TeamsBot MicrosoftAppId as the SPA client.");
            }

            var botId = _teamsBot.MicrosoftAppId?.Trim();
            if (!string.IsNullOrWhiteSpace(botId))
            {
                if (string.Equals(spaClientId, botId, StringComparison.OrdinalIgnoreCase))
                {
                    setupHints.Insert(0,
                        "MSAL clientId matches TeamsBot:MicrosoftAppId (Azure Bot). " +
                        "That app is not a SPA — Sign in with Microsoft will fail. " +
                        "Set AzureAd:SpaClientId to your SPA/Graph app (redirect URI http://localhost:4200/login) " +
                        "and clear any user-secret/env override that sets AzureAd:ClientId to the Bot App Id.");
                }
                else if (hasApiClient
                         && string.Equals(_azureAd.ClientId, botId, StringComparison.OrdinalIgnoreCase))
                {
                    setupHints.Insert(0,
                        "AzureAd:ClientId matches TeamsBot:MicrosoftAppId. " +
                        "Point ClientId/ApiScope at the API app registration, not the Bot.");
                }
            }

            setupHints.Add(
                "In Entra → SPA app → Authentication → add redirect URI exactly: " +
                "http://localhost:4200/login (and your production https://…/login).");
            setupHints.Add(
                "Expose API scope on the API app (AzureAd:ClientId), e.g. api://{ClientId}/access_as_user, " +
                "then grant that permission to the SPA app and admin-consent.");
        }

        return Ok(new
        {
            entraEnabled = entraConfigured,
            tenantId = entraConfigured ? _azureAd.TenantId : null,
            clientId = entraConfigured ? spaClientId : null,
            apiClientId = hasApiClient ? _azureAd.ClientId : null,
            apiScope = entraConfigured ? apiScope : null,
            authority = entraConfigured
                ? $"{_azureAd.Instance.TrimEnd('/')}/{_azureAd.TenantId}"
                : null,
            redirectUri = "/login",
            spaFallsBackToApiClient,
            setupHints,
            enforceAcl = _azureAd.EnforceAcl,
            requireAuthOnApi = _azureAd.RequireAuthOnApi,
            allowPilotAdminLogin = _azureAd.AllowPilotAdminLogin
        });
    }

    [HttpGet("me")]
    public ActionResult<object> Me([FromHeader(Name = "X-Bd-Admin-Token")] string? token)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var oid = UserIdentity.ResolveObjectId(User);
            var name = User.FindFirst("name")?.Value
                       ?? User.FindFirst("preferred_username")?.Value
                       ?? oid;
            var roles = User.FindAll("roles").Select(c => c.Value)
                .Concat(User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value))
                .Distinct()
                .ToList();
            return Ok(new
            {
                username = User.FindFirst("preferred_username")?.Value ?? oid,
                displayName = name,
                role = roles.Contains("BdCopilot.Admin") ? "Admin" : (roles.FirstOrDefault() ?? "User"),
                objectId = oid,
                authMode = "entra"
            });
        }

        if (string.IsNullOrWhiteSpace(token) || !TryParse(token, out var username, out var displayName, out var role, out var exp))
        {
            return Unauthorized();
        }

        if (exp < DateTimeOffset.UtcNow)
        {
            return Unauthorized(new { title = "Session expired." });
        }

        return Ok(new { username, displayName, role, expiresAt = exp, authMode = "pilot" });
    }

    internal static bool TryParse(
        string token,
        out string username,
        out string displayName,
        out string role,
        out DateTimeOffset expiresAt)
    {
        username = "";
        displayName = "";
        role = "";
        expiresAt = default;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(token));
            using var doc = JsonDocument.Parse(json);
            username = doc.RootElement.GetProperty("sub").GetString() ?? "";
            displayName = doc.RootElement.GetProperty("name").GetString() ?? username;
            role = doc.RootElement.GetProperty("role").GetString() ?? "Admin";
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(doc.RootElement.GetProperty("exp").GetInt64());
            return !string.IsNullOrWhiteSpace(username);
        }
        catch
        {
            return false;
        }
    }
}
