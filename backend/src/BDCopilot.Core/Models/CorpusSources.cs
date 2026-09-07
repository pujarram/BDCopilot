namespace BDCopilot.Core.Models;

/// <summary>
/// Which document corpus retrieval/generation should use.
/// Local = files under BD_Copilot/docs; Online = SharePoint / Graph-synced (and pilot seed);
/// Planner = indexed Microsoft Planner task text.
/// </summary>
public static class CorpusSources
{
    public const string Local = "Local";
    public const string Online = "Online";
    public const string Planner = "Planner";
    public const string Battlecards = "Battlecards";
    public const string All = "All";

    /// <summary>Graph drive id stamped on every locally indexed file.</summary>
    public const string LocalDriveId = "local-docs";

    /// <summary>Graph drive id stamped on every Planner-indexed task.</summary>
    public const string PlannerDriveId = "planner-tasks";

    /// <summary>Drive id for curated competitive battlecards corpus.</summary>
    public const string BattlecardsDriveId = "battlecards";

    public static string Normalize(string? value) =>
        value?.Trim() switch
        {
            Local => Local,
            Online => Online,
            Planner => Planner,
            Battlecards => Battlecards,
            All => All,
            _ => Online
        };

    public static bool IsLocalDocument(Document doc) =>
        string.Equals(doc.GraphDriveId, LocalDriveId, StringComparison.OrdinalIgnoreCase);

    public static bool IsPlannerDocument(Document doc) =>
        string.Equals(doc.GraphDriveId, PlannerDriveId, StringComparison.OrdinalIgnoreCase);

    public static bool IsBattlecardsDocument(Document doc) =>
        string.Equals(doc.GraphDriveId, BattlecardsDriveId, StringComparison.OrdinalIgnoreCase);

    public static bool IsOnlineDocument(Document doc) =>
        !IsLocalDocument(doc) && !IsPlannerDocument(doc) && !IsBattlecardsDocument(doc);
}
