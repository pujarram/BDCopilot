using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using Microsoft.Extensions.Options;

namespace BDCopilot.Infrastructure.Services;

/// <summary>
/// Dynamics 365 / Dataverse deal context for generators.
/// Demo seed is default; live Dataverse wiring uses configured URL + app credentials.
/// </summary>
public sealed class DynamicsDealService : IDynamicsDealService
{
    private readonly DynamicsSettings _settings;

    private static readonly List<DynamicsDealContext> DemoDeals =
    [
        new()
        {
            OpportunityId = "demo-opp-meridian",
            Name = "Wealth platform modernization",
            Client = "Meridian Private Bank",
            EstimatedValue = 2_400_000,
            Stage = "Propose",
            Owner = "Alex Rivera",
            CloseDate = DateTimeOffset.UtcNow.AddDays(45),
            FocusNotes =
                "Dynamics deal context: Meridian Private Bank — wealth platform modernization, €2.4M, Propose stage. " +
                "Priorities: SOC2 evidence, EU data residency, reuse IWM RFP language on omnichannel advisor experience.",
            FromDemoSeed = true
        },
        new()
        {
            OpportunityId = "demo-opp-nordic",
            Name = "Core banking AI assistant",
            Client = "Nordic Retail Bank",
            EstimatedValue = 1_100_000,
            Stage = "Qualify",
            Owner = "Sam Okonkwo",
            CloseDate = DateTimeOffset.UtcNow.AddDays(90),
            FocusNotes =
                "Dynamics deal context: Nordic Retail Bank — AI assistant for branch + digital, €1.1M, Qualify. " +
                "Emphasize Finnish/Nordic compliance pack and battlecard vs incumbent chatbot vendor.",
            FromDemoSeed = true
        }
    ];

    public DynamicsDealService(IOptions<DynamicsSettings> settings) => _settings = settings.Value;

    public Task<List<DynamicsDealContext>> ListDemoDealsAsync(CancellationToken ct = default) =>
        Task.FromResult(DemoDeals.ToList());

    public Task<DynamicsDealContext?> GetDealContextAsync(
        string? dynamicsOpportunityId,
        string? clientName,
        CancellationToken ct = default)
    {
        if (_settings.UseDemoSeed || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.DataverseUrl))
        {
            DynamicsDealContext? match = null;
            if (!string.IsNullOrWhiteSpace(dynamicsOpportunityId))
            {
                match = DemoDeals.FirstOrDefault(d =>
                    string.Equals(d.OpportunityId, dynamicsOpportunityId, StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && !string.IsNullOrWhiteSpace(clientName))
            {
                match = DemoDeals.FirstOrDefault(d =>
                    d.Client.Contains(clientName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<DynamicsDealContext?>(match ?? DemoDeals[0]);
        }

        // Live Dataverse OData would be called here with app credentials.
        // Until secrets are configured, fall back to demo seed.
        return Task.FromResult<DynamicsDealContext?>(DemoDeals[0]);
    }
}
