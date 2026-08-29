using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public class TokenUsageTracker : ITokenUsageTracker
{
    private readonly BdCopilotDbContext _db;
    private readonly ILogger<TokenUsageTracker> _logger;
    private readonly TelemetryClient? _telemetry;

    public TokenUsageTracker(
        BdCopilotDbContext db,
        ILogger<TokenUsageTracker> logger,
        TelemetryClient? telemetry = null)
    {
        _db = db;
        _logger = logger;
        _telemetry = telemetry;
    }

    public async Task TrackAsync(TokenUsageRecord record, CancellationToken ct = default)
    {
        if (record.TotalTokens == 0)
        {
            record.TotalTokens = record.PromptTokens + record.CompletionTokens;
        }

        _db.TokenUsageRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Token usage: {Operation} {Provider}/{Model} user={UserObjectId} total={TotalTokens}",
            record.Operation, record.Provider, record.Model, record.UserObjectId, record.TotalTokens);

        try
        {
            _telemetry?.TrackEvent("BdCopilot.TokenUsage", new Dictionary<string, string>
            {
                ["UserObjectId"] = record.UserObjectId,
                ["TeamId"] = record.TeamId ?? "",
                ["Initiative"] = record.Initiative ?? "",
                ["Operation"] = record.Operation,
                ["Provider"] = record.Provider,
                ["Model"] = record.Model
            }, new Dictionary<string, double>
            {
                ["PromptTokens"] = record.PromptTokens,
                ["CompletionTokens"] = record.CompletionTokens,
                ["TotalTokens"] = record.TotalTokens
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Application Insights token usage event failed (non-fatal).");
        }
    }
}
