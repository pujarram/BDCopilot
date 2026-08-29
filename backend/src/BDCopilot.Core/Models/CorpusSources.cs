namespace BDCopilot.Core.Models;

/// <summary>
/// Which document corpus retrieval/generation should use.
/// Local = files under BD_Copilot/docs; Online = SharePoint / Graph-synced (and pilot seed).
/// </summary>
public static class CorpusSources
{
    public const string Local = "Local";
    public const string Online = "Online";
    public const string All = "All";

    /// <summary>Graph drive id stamped on every locally indexed file.</summary>
    public const string LocalDriveId = "local-docs";

    public static string Normalize(string? value) =>
        value?.Trim() switch
        {
            Local => Local,
            Online => Online,
            All => All,
            _ => Online
        };

    public static bool IsLocalDocument(Document doc) =>
        string.Equals(doc.GraphDriveId, LocalDriveId, StringComparison.OrdinalIgnoreCase);
}
