namespace BDCopilot.Infrastructure.Services;

/// <summary>Microsoft Planner sync settings (Project Intelligence Phase 1).</summary>
public class PlannerSyncSettings
{
    public const string SectionName = "Planner";

    public bool Enabled { get; set; } = true;

    /// <summary>When Graph Planner is unavailable, seed a demo Wealth Copilot plan.</summary>
    public bool SeedDemoData { get; set; } = true;

    /// <summary>M365 group ids that host Planner plans.</summary>
    public List<string> GroupIds { get; set; } = [];

    /// <summary>Explicit plan ids (optional; discovered via groups when empty).</summary>
    public List<string> PlanIds { get; set; } = [];
}
