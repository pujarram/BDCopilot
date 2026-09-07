using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

public sealed class PartnerOnboardingService : IPartnerOnboardingService
{
    private readonly CustomerRolloutSettings _customer;
    private readonly IOptions<GraphSyncSettings> _graph;
    private readonly IOptions<PlannerSyncSettings> _planner;
    private readonly IOptions<TeamsBotSettings> _teams;

    public PartnerOnboardingService(
        IOptions<CustomerRolloutSettings> customer,
        IOptions<GraphSyncSettings> graph,
        IOptions<PlannerSyncSettings> planner,
        IOptions<TeamsBotSettings> teams)
    {
        _customer = customer.Value;
        _graph = graph;
        _planner = planner;
        _teams = teams;
    }

    public Task<PartnerOnboardingProfile> GetProfileAsync(CancellationToken ct = default)
    {
        return Task.FromResult(new PartnerOnboardingProfile
        {
            Code = _customer.Code,
            DisplayName = _customer.DisplayName,
            TenantId = _graph.Value.TenantId,
            PilotSitePath = _graph.Value.PilotSitePath,
            PlannerGroupIds = _planner.Value.GroupIds.Where(g => !string.IsNullOrWhiteSpace(g)).ToList(),
            GraphAppId = _graph.Value.ClientId,
            TeamsManifestBaseUrl = _teams.Value.WebBaseUrl
        });
    }

    public Task<PartnerOnboardingProfile> SaveProfileAsync(PartnerOnboardingProfile profile, CancellationToken ct = default)
    {
        // Profile persistence is via Key Vault / appsettings in production; return merged view for wizard.
        profile.TeamsManifestBaseUrl = string.IsNullOrWhiteSpace(profile.TeamsManifestBaseUrl)
            ? _teams.Value.WebBaseUrl
            : profile.TeamsManifestBaseUrl;
        return Task.FromResult(profile);
    }

    public Task<string> GenerateTeamsManifestAsync(CancellationToken ct = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_teams.Value.WebBaseUrl)
            ? "https://localhost:4200"
            : _teams.Value.WebBaseUrl.TrimEnd('/');
        var appId = string.IsNullOrWhiteSpace(_teams.Value.MicrosoftAppId)
            ? "00000000-0000-0000-0000-000000000000"
            : _teams.Value.MicrosoftAppId;
        var host = Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : "localhost";
        var manifest = $$"""
        {
          "$schema": "https://developer.microsoft.com/en-us/json-schemas/teams/v1.17/MicrosoftTeams.schema.json",
          "manifestVersion": "1.17",
          "version": "1.0.0",
          "id": "{{appId}}",
          "packageName": "com.bdcopilot.teams",
          "developer": {
            "name": "BD Copilot",
            "websiteUrl": "{{baseUrl}}",
            "privacyUrl": "{{baseUrl}}/privacy",
            "termsOfUseUrl": "{{baseUrl}}/terms"
          },
          "name": { "short": "BD Copilot", "full": "BD Copilot — pursuit & delivery" },
          "description": {
            "short": "Grounded BD generation and Project Intelligence",
            "full": "Chat, RFP generation, knowledge search, and Planner-backed delivery insights."
          },
          "icons": { "color": "color.png", "outline": "outline.png" },
          "accentColor": "#021E57",
          "bots": [{
            "botId": "{{appId}}",
            "scopes": ["personal", "team", "groupChat"],
            "supportsFiles": false,
            "isNotificationOnly": false,
            "commandLists": [{
              "scopes": ["personal", "team", "groupChat"],
              "commands": [
                { "title": "help", "description": "Show Project Intelligence commands" },
                { "title": "stalled", "description": "Tasks with no progress for 7+ days" },
                { "title": "unified", "description": "Unified delivery + docs brief" }
              ]
            }]
          }],
          "staticTabs": [{
            "entityId": "dashboard",
            "name": "Dashboard",
            "contentUrl": "{{baseUrl}}/dashboard",
            "websiteUrl": "{{baseUrl}}/dashboard",
            "scopes": ["personal"]
          }],
          "validDomains": ["{{host}}"]
        }
        """;
        return Task.FromResult(manifest);
    }
}
