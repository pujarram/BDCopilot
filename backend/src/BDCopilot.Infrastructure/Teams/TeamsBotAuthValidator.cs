using System.IdentityModel.Tokens.Jwt;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OidcConfigurationManager = Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>;

namespace BDCopilot.Infrastructure.Teams;

/// <summary>
/// Validates inbound Teams / Bot Framework requests via webhook secret (dev)
/// or Bot Framework JWT when MicrosoftAppId is configured.
/// Does not reference ASP.NET Core types so it can live in Infrastructure.
/// </summary>
public sealed class TeamsBotAuthValidator
{
    private const string BotFrameworkMetadata = "https://login.botframework.com/v1/.well-known/openidconfiguration";
    private static readonly HttpClient MetadataHttp = new();

    private readonly TeamsBotSettings _settings;
    private readonly GraphSyncSettings _graph;
    private readonly IHostEnvironment _env;
    private readonly ILogger<TeamsBotAuthValidator> _logger;
    private OidcConfigurationManager? _oidcManager;

    public TeamsBotAuthValidator(
        IOptions<TeamsBotSettings> settings,
        IOptions<GraphSyncSettings> graph,
        IHostEnvironment env,
        ILogger<TeamsBotAuthValidator> logger)
    {
        _settings = settings.Value;
        _graph = graph.Value;
        _env = env;
        _logger = logger;
    }

    public bool IsEnabled() => _settings.Enabled;

    public string ResolveAppId() =>
        FirstNonEmpty(_settings.MicrosoftAppId, _graph.ClientId);

    public async Task<bool> ValidateAsync(
        string? authorizationHeader,
        string? teamsWebhookSecretHeader,
        string? webhookSecretHeader,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled())
        {
            return false;
        }

        var webhookSecret = _settings.WebhookSecret?.Trim();
        if (!string.IsNullOrWhiteSpace(webhookSecret))
        {
            if (string.Equals(teamsWebhookSecretHeader, webhookSecret, StringComparison.Ordinal)
                || string.Equals(webhookSecretHeader, webhookSecret, StringComparison.Ordinal))
            {
                return true;
            }
        }

        var appId = ResolveAppId();
        if (string.IsNullOrWhiteSpace(appId))
        {
            if (_env.IsDevelopment())
            {
                _logger.LogWarning("Teams bot: no WebhookSecret or MicrosoftAppId — allowing in Development only.");
                return true;
            }

            return false;
        }

        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            _oidcManager ??= new OidcConfigurationManager(
                BotFrameworkMetadata,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(MetadataHttp) { RequireHttps = true });

            var oidc = await _oidcManager.GetConfigurationAsync(cancellationToken);
            var handler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                IssuerValidator = (issuer, _, _) =>
                {
                    if (issuer.StartsWith("https://api.botframework.com", StringComparison.OrdinalIgnoreCase)
                        || issuer.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase)
                        || issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase))
                    {
                        return issuer;
                    }

                    throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}");
                },
                ValidateAudience = true,
                ValidAudience = appId,
                ValidateLifetime = true,
                IssuerSigningKeys = oidc.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            handler.ValidateToken(token, parameters, out _);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teams Bot Framework JWT validation failed.");
            return false;
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";
}
