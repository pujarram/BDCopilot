using Azure.Identity;
using BDCopilot.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;

namespace BDCopilot.Infrastructure.Graph;

/// <summary>
/// Creates a GraphServiceClient from app-only credentials when Graph settings are complete;
/// returns null when not configured so callers can skip Graph operations gracefully.
/// </summary>
public class GraphClientFactory
{
    private readonly GraphSyncSettings _settings;
    private readonly ILogger<GraphClientFactory> _logger;
    private GraphServiceClient? _client;

    public GraphClientFactory(IOptions<GraphSyncSettings> settings, ILogger<GraphClientFactory> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.TenantId) &&
        !string.IsNullOrWhiteSpace(_settings.ClientId) &&
        !string.IsNullOrWhiteSpace(_settings.ClientSecret);

    public GraphServiceClient? GetClient()
    {
        if (!IsConfigured)
        {
            return null;
        }

        if (_client is not null)
        {
            return _client;
        }

        try
        {
            var credential = new ClientSecretCredential(
                _settings.TenantId,
                _settings.ClientId,
                _settings.ClientSecret);

            _client = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
            return _client;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create GraphServiceClient — Graph operations will be skipped.");
            return null;
        }
    }
}
