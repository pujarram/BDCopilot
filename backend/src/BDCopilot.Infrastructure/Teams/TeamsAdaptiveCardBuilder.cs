using System.Text.Json.Nodes;

namespace BDCopilot.Infrastructure.Teams;

/// <summary>Builds Adaptive Card payloads for Teams bot Project Intelligence replies.</summary>
public static class TeamsAdaptiveCardBuilder
{
    public static JsonObject HealthSummaryCard(
        int healthScore,
        string riskLevel,
        int totalTasks,
        int delayed,
        double completionPercent,
        string dashboardUrl)
    {
        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                TextBlock("Project Intelligence", "Large", true),
                FactSet(
                    ("Health score", $"{healthScore}/100"),
                    ("Risk", riskLevel),
                    ("Tasks", totalTasks.ToString()),
                    ("Delayed", delayed.ToString()),
                    ("Completion", $"{completionPercent:0.#}%")),
                TextBlock("Open the dashboard for Gantt, workload, and unified briefs.", "Small")
            },
            ["actions"] = Actions(dashboardUrl, "Open dashboard")
        };
    }

    public static JsonObject UnifiedBriefCard(
        int healthScore,
        string riskLevel,
        string summaryLine,
        int delayedCount,
        int relatedDocCount,
        string dashboardUrl)
    {
        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                TextBlock("Unified intelligence brief", "Large", true),
                FactSet(
                    ("Health", $"{healthScore}/100"),
                    ("Risk", riskLevel),
                    ("Delayed tasks", delayedCount.ToString()),
                    ("Related docs", relatedDocCount.ToString())),
                TextBlock(Truncate(summaryLine, 280), "Default", false)
            },
            ["actions"] = Actions(dashboardUrl, "Open Project Intelligence")
        };
    }

    public static JsonObject DelayedTasksCard(int delayedCount, string topTasksText, string dashboardUrl)
    {
        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                TextBlock("Delayed tasks", "Large", true),
                TextBlock($"{delayedCount} overdue item(s)", "Medium", true),
                TextBlock(Truncate(topTasksText, 320), "Default", false)
            },
            ["actions"] = Actions(dashboardUrl, "View in dashboard")
        };
    }

    private static JsonObject TextBlock(string text, string size, bool bold = false) =>
        new()
        {
            ["type"] = "TextBlock",
            ["text"] = text,
            ["size"] = size,
            ["wrap"] = true,
            ["weight"] = bold ? "Bolder" : "Default"
        };

    private static JsonObject FactSet(params (string title, string value)[] rows)
    {
        var facts = new JsonArray();
        foreach (var (title, value) in rows)
        {
            facts.Add(new JsonObject
            {
                ["title"] = title,
                ["value"] = value
            });
        }

        return new JsonObject { ["type"] = "FactSet", ["facts"] = facts };
    }

    private static JsonArray Actions(string url, string title) =>
        new()
        {
            new JsonObject
            {
                ["type"] = "Action.OpenUrl",
                ["title"] = title,
                ["url"] = url
            }
        };

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) ? ""
        : text.Length <= max ? text
        : text[..max].TrimEnd() + "…";
}
