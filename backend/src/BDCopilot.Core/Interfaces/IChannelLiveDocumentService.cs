using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

/// <summary>
/// On-demand read of files in the configured Teams channel document library (Graph drive),
/// merged with indexed RAG when answering Teams chat questions.
/// </summary>
public interface IChannelLiveDocumentService
{
    Task<IReadOnlyList<SearchResultItem>> SearchLiveChannelAsync(
        string query,
        string userObjectId,
        CancellationToken ct = default);
}
