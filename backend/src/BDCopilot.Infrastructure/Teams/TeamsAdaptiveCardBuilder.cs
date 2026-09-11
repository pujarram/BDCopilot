using System.Text.Json.Nodes;
using BDCopilot.Core.Models;

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

    public static JsonObject StalledAlertCard(
        IReadOnlyList<PlannerStalledAlert> alerts,
        string dashboardUrl)
    {
        var body = new JsonArray
        {
            TextBlock("Progress stalled (7+ days)", "Large", true),
            TextBlock($"{alerts.Count} task(s) unchanged", "Medium", true)
        };

        foreach (var a in alerts.Take(5))
        {
            body.Add(FactSet(
                (a.Title, $"{a.PercentComplete}% · {a.StalledDays}d"),
                ("Owner", a.AssignedUsers ?? "Unassigned")));
        }

        if (alerts.Count > 5)
        {
            body.Add(TextBlock($"+ {alerts.Count - 5} more in dashboard", "Small"));
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = Actions(dashboardUrl, "Review stalled work")
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

    public static JsonObject PursuitDeadlineCard(
        IReadOnlyList<PursuitDeadlineAlert> alerts,
        string dashboardUrl)
    {
        var body = new JsonArray
        {
            TextBlock("Pursuit deadlines", "Large", true),
            TextBlock($"{alerts.Count} pursuit(s) due within 14 days", "Medium", true)
        };

        foreach (var a in alerts.Take(5))
        {
            body.Add(FactSet(
                (a.Name, $"{a.DaysRemaining}d · {a.Stage}"),
                ("Client", a.Client),
                ("Owner", a.OwnerDisplayName ?? "Unassigned")));
        }

        if (alerts.Count > 5)
        {
            body.Add(TextBlock($"+ {alerts.Count - 5} more in dashboard", "Small"));
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = Actions(dashboardUrl, "Review pipeline")
        };
    }

    public static JsonObject StaleDocumentCard(
        IReadOnlyList<StaleDocumentAlert> alerts,
        string libraryUrl)
    {
        var body = new JsonArray
        {
            TextBlock("Stale library documents", "Large", true),
            TextBlock($"{alerts.Count} document(s) need refresh", "Medium", true)
        };

        foreach (var a in alerts.Take(5))
        {
            body.Add(FactSet(
                (a.FileName, $"{a.MonthsSinceModified} months old"),
                ("Corpus", a.CorpusSource)));
        }

        if (alerts.Count > 5)
        {
            body.Add(TextBlock($"+ {alerts.Count - 5} more in library", "Small"));
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = Actions(libraryUrl, "Open document library")
        };
    }

    /// <summary>Adaptive Card for grounded chat/search answers with openable source links.</summary>
    public static JsonObject? GroundedSourcesCard(
        string answerPreview,
        IReadOnlyList<Citation> citations,
        string apiBaseUrl,
        string userObjectId,
        string? chatTabUrl = null)
    {
        if (citations.Count == 0)
        {
            return null;
        }

        var body = new JsonArray
        {
            TextBlock("BD Copilot answer", "Large", true),
            TextBlock(Truncate(answerPreview, 420), "Default", false),
            TextBlock("Sources", "Medium", true)
        };

        foreach (var c in citations.Take(4))
        {
            var label = CitationOpenUrl.FormatLabel(c);
            var snippet = string.IsNullOrWhiteSpace(c.Snippet) ? null : Truncate(c.Snippet, 160);
            body.Add(FactSet(
                (label, snippet ?? "Open indexed source")));
        }

        if (citations.Count > 4)
        {
            body.Add(TextBlock($"+ {citations.Count - 4} more in chat", "Small"));
        }

        var actions = new JsonArray();
        foreach (var c in citations.Take(3))
        {
            var url = CitationOpenUrl.Resolve(c, apiBaseUrl, userObjectId);
            if (url is null) continue;
            var title = Truncate(c.FileName, 28);
            actions.Add(new JsonObject
            {
                ["type"] = "Action.OpenUrl",
                ["title"] = $"Open {title}",
                ["url"] = url
            });
        }

        if (!string.IsNullOrWhiteSpace(chatTabUrl))
        {
            actions.Add(new JsonObject
            {
                ["type"] = "Action.OpenUrl",
                ["title"] = "Open BD Copilot",
                ["url"] = chatTabUrl
            });
        }

        if (actions.Count == 0)
        {
            return null;
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = actions
        };
    }

    /// <summary>Battle card objections + win themes for field sellers in Teams.</summary>
    public static JsonObject BattleCardObjectionsCard(
        string title,
        string competitor,
        IReadOnlyList<(string Objection, string Response)> objections,
        IReadOnlyList<string> winThemes,
        string battleCardUrl)
    {
        var body = new JsonArray
        {
            TextBlock(Truncate(title, 80), "Large", true),
            TextBlock($"vs {Truncate(competitor, 60)}", "Medium", true)
        };

        if (winThemes.Count > 0)
        {
            body.Add(TextBlock("Win themes", "Medium", true));
            foreach (var theme in winThemes.Take(4))
            {
                body.Add(TextBlock($"• {Truncate(theme, 160)}", "Default"));
            }
        }

        if (objections.Count > 0)
        {
            body.Add(TextBlock("Objection handling", "Medium", true));
            foreach (var (objection, response) in objections.Take(4))
            {
                body.Add(FactSet(
                    ("Objection", Truncate(objection, 180)),
                    ("Response", Truncate(response, 220))));
            }
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = Actions(battleCardUrl, "Open Battle Card")
        };
    }

    /// <summary>Quick-ask chips — messageBack posts the prompt as a normal chat message.</summary>
    public static JsonObject QuickPromptChipsCard(
        IReadOnlyList<(string Label, string Prompt)> chips)
    {
        var actions = new JsonArray();
        foreach (var (label, prompt) in chips.Take(8))
        {
            actions.Add(MessageBackAction(label, prompt, prompt));
        }

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                TextBlock("Quick asks", "Large", true),
                TextBlock("Tap a chip to ask BD Copilot — grounded answer with citations.", "Default")
            },
            ["actions"] = actions
        };
    }

    /// <summary>Confirm / Cancel before running a document generator in Teams.</summary>
    public static JsonObject GeneratorConfirmCard(
        string generatorLabel,
        string topic,
        string confirmCommand,
        string cancelCommand,
        string openTabUrl)
    {
        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                TextBlock($"Generate {generatorLabel}?", "Large", true),
                FactSet(
                    ("Generator", generatorLabel),
                    ("Topic", Truncate(topic, 160))),
                TextBlock(
                    "Confirm to draft with grounded sources. Or open the full generator tab for more controls.",
                    "Default")
            },
            ["actions"] = new JsonArray
            {
                MessageBackAction("Confirm", $"Confirm {generatorLabel}", confirmCommand),
                MessageBackAction("Cancel", "Cancel generation", cancelCommand),
                new JsonObject
                {
                    ["type"] = "Action.OpenUrl",
                    ["title"] = "Open full generator",
                    ["url"] = openTabUrl
                }
            }
        };
    }

    /// <summary>Short result card after Teams generation completes.</summary>
    public static JsonObject GeneratorResultCard(
        string title,
        int sectionCount,
        string preview,
        string openTabUrl)
    {
        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = new JsonArray
            {
                TextBlock(Truncate(title, 90), "Large", true),
                FactSet(
                    ("Sections", sectionCount.ToString()),
                    ("Status", "Draft")),
                TextBlock(Truncate(preview, 420), "Default")
            },
            ["actions"] = Actions(openTabUrl, "Open generator")
        };
    }

    /// <summary>Legal / Sales approval reminder with Approve / Reject actions.</summary>
    public static JsonObject ApprovalReminderCard(
        string documentTitle,
        Guid generationId,
        IReadOnlyList<(string Role, string Status)> reviews,
        string openTabUrl)
    {
        var body = new JsonArray
        {
            TextBlock("Approval reminder", "Large", true),
            TextBlock(Truncate(documentTitle, 100), "Medium", true),
            FactSet(reviews.Select(r => (r.Role, r.Status)).ToArray()),
        };

        var actions = new JsonArray();
        foreach (var (role, status) in reviews)
        {
            if (!string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            actions.Add(MessageBackAction(
                $"Approve ({role})",
                $"Approve as {role}",
                $"approve {generationId:D} as {role}"));
            actions.Add(MessageBackAction(
                $"Reject ({role})",
                $"Reject as {role}",
                $"reject {generationId:D} as {role}"));
        }

        actions.Add(new JsonObject
        {
            ["type"] = "Action.OpenUrl",
            ["title"] = "Open draft",
            ["url"] = openTabUrl
        });

        return new JsonObject
        {
            ["type"] = "AdaptiveCard",
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["version"] = "1.5",
            ["body"] = body,
            ["actions"] = actions
        };
    }

    private static JsonObject MessageBackAction(string title, string displayText, string text) =>
        new()
        {
            ["type"] = "Action.Submit",
            ["title"] = title,
            ["data"] = new JsonObject
            {
                ["msteams"] = new JsonObject
                {
                    ["type"] = "messageBack",
                    ["displayText"] = displayText,
                    ["text"] = text,
                    ["value"] = text
                }
            }
        };

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
