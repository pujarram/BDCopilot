using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface ITeamsChannelService
{
    Task<IReadOnlyList<TeamsReply>> ProcessActivityAsync(TeamsActivity activity, CancellationToken ct = default);
}

public interface ITeamsBotConnector
{
    string? LastDeliveryStatus { get; }

    IReadOnlyList<string> GetRecentErrors(int take = 5);

    bool CanSendViaConnector(TeamsActivity inbound);

    Task SendTypingAsync(TeamsActivity inbound, CancellationToken ct = default);

    Task<bool> SendRepliesAsync(TeamsActivity inbound, IReadOnlyList<TeamsReply> replies, CancellationToken ct = default);
}
