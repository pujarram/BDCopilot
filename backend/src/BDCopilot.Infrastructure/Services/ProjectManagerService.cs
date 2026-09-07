using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace BDCopilot.Infrastructure.Services;

public sealed class ProjectManagerService : IProjectManagerService
{
    private readonly BdCopilotDbContext _db;
    private readonly IAiChatService _chat;
    private readonly ILogger<ProjectManagerService> _logger;

    public ProjectManagerService(
        BdCopilotDbContext db,
        IAiChatService chat,
        ILogger<ProjectManagerService> logger)
    {
        _db = db;
        _chat = chat;
        _logger = logger;
    }

    public async Task<ProjectManagerInsight> GetInsightsAsync(CancellationToken ct = default)
    {
        var tasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);
        var today = DateTime.UtcNow.Date;

        var score = ComputeHealthScore(tasks);
        var risk = ScoreToRisk(score, tasks.Count(t => t.IsDelayed));
        var predictions = PredictDelays(tasks, today);
        var modules = AssessModules(tasks);
        var staffing = RecommendStaffing(tasks, modules);
        var healthExplanation = BuildHealthExplanation(tasks, score, risk);

        var summary =
            $"Health score **{score}/100** ({risk}). " +
            $"{tasks.Count(t => t.IsDelayed)} delayed · {predictions.Count(p => p.RiskLevel is "High" or "Medium")} at risk of slip · " +
            $"{modules.Count(m => m.RiskLevel is "High" or "Medium")} modules need attention.";

        return new ProjectManagerInsight
        {
            HealthScore = score,
            RiskLevel = risk,
            Summary = summary,
            GeneratedAt = DateTimeOffset.UtcNow,
            DelayPredictions = predictions,
            ModulesAtRisk = modules,
            StaffingRecommendations = staffing,
            HealthExplanation = healthExplanation
        };
    }

    public async Task<StakeholderWeeklyReport> GenerateStakeholderReportAsync(
        bool useLlmNarrative = true,
        string? userObjectId = null,
        CancellationToken ct = default)
    {
        var insight = await GetInsightsAsync(ct);
        var healthTasks = await _db.PlannerTasks.AsNoTracking().ToListAsync(ct);
        var completed = healthTasks.Count(t => t.PercentComplete >= 100 || t.Status == "Completed");
        var inProgress = healthTasks.Count(t => t.PercentComplete is > 0 and < 100);

        var facts = new StringBuilder();
        facts.AppendLine($"Week of {DateTime.UtcNow:yyyy-MM-dd} UTC");
        facts.AppendLine($"Health score: {insight.HealthScore}/100 · Risk: {insight.RiskLevel}");
        facts.AppendLine($"Tasks: total={healthTasks.Count}, completed={completed}, inProgress={inProgress}, delayed={healthTasks.Count(t => t.IsDelayed)}");
        facts.AppendLine();
        facts.AppendLine("Top delay risks:");
        foreach (var p in insight.DelayPredictions.Take(8))
        {
            facts.AppendLine($"- {p.Title} | slip≈{p.PredictedSlipDays}d | {p.RiskLevel} | {p.Rationale}");
        }

        facts.AppendLine();
        facts.AppendLine("Modules at risk:");
        foreach (var m in insight.ModulesAtRisk.Take(6))
        {
            facts.AppendLine($"- {m.ModuleName}: delayed={m.DelayedCount}/{m.TaskCount}, avg%={m.AvgPercentComplete:0} → {m.Recommendation}");
        }

        facts.AppendLine();
        facts.AppendLine("Staffing:");
        foreach (var s in insight.StaffingRecommendations.Take(6))
        {
            facts.AppendLine($"- [{s.Priority}] {s.Focus}: {s.Detail}");
        }

        var body = BuildDeterministicReport(insight, facts.ToString());
        var usedLlm = false;
        string? provider = null;
        string? model = null;

        if (useLlmNarrative)
        {
            try
            {
                var reply = await _chat.AskAsync(new ChatRequest
                {
                    Message =
                        "Write a concise stakeholder weekly status (8–14 short bullet lines) for executives. " +
                        "Cover health, risks, modules at risk, and staffing asks. No fluff. Use only PLANNER_SNAPSHOT.",
                    UserObjectId = string.IsNullOrWhiteSpace(userObjectId)
                        ? "project-manager-report"
                        : userObjectId,
                    History = [],
                    ExtraContext = facts.ToString()
                }, ct);

                if (!string.IsNullOrWhiteSpace(reply.Answer))
                {
                    body =
                        $"# Stakeholder weekly report\n\n" +
                        $"**Health score:** {insight.HealthScore}/100 · **Risk:** {insight.RiskLevel}\n\n" +
                        reply.Answer.Trim() +
                        "\n\n---\n_Generated from live Planner snapshot._";
                    usedLlm = true;
                    provider = reply.AiProvider;
                    model = reply.Model;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM stakeholder narrative failed; using deterministic report.");
            }
        }

        return new StakeholderWeeklyReport
        {
            Title = $"BD Project Intelligence — weekly status ({DateTime.UtcNow:yyyy-MM-dd})",
            MarkdownBody = body,
            HealthScore = insight.HealthScore,
            RiskLevel = insight.RiskLevel,
            GeneratedAt = DateTimeOffset.UtcNow,
            UsedLlmNarrative = usedLlm,
            AiProvider = provider,
            Model = model
        };
    }

    public async Task<Stream> ExportStakeholderReportDocxAsync(
        bool useLlmNarrative = true,
        string? userObjectId = null,
        CancellationToken ct = default)
    {
        var report = await GenerateStakeholderReportAsync(useLlmNarrative, userObjectId, ct);
        var stream = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body());
            AddParagraph(mainPart, report.Title, bold: true, size: 28);
            AddParagraph(mainPart, $"Health {report.HealthScore}/100 · Risk {report.RiskLevel} · {report.GeneratedAt:u}");
            foreach (var line in report.MarkdownBody.Split('\n'))
            {
                var t = line.Trim();
                if (string.IsNullOrWhiteSpace(t)) continue;
                t = t.TrimStart('#').TrimStart('*').Trim();
                if (t.StartsWith('-') || t.StartsWith('•'))
                {
                    AddParagraph(mainPart, "• " + t.TrimStart('-', '•', ' ').Trim());
                }
                else
                {
                    AddParagraph(mainPart, t, bold: line.TrimStart().StartsWith('#'));
                }
            }

            mainPart.Document.Save();
        }

        stream.Position = 0;
        return stream;
    }

    private static string BuildDeterministicReport(ProjectManagerInsight insight, string facts) =>
        $"""
        # Stakeholder weekly report

        **Health score:** {insight.HealthScore}/100 · **Risk:** {insight.RiskLevel}

        {insight.Summary}

        ## Snapshot
        {facts}
        """;

    internal static int ComputeHealthScore(IReadOnlyList<PlannerTaskItem> tasks)
    {
        if (tasks.Count == 0) return 0;

        var delayed = tasks.Count(t => t.IsDelayed);
        var completed = tasks.Count(t => t.PercentComplete >= 100 || t.Status == "Completed");
        var notStarted = tasks.Count(t => t.PercentComplete == 0 && t.Status != "Completed");
        var completion = 100.0 * completed / tasks.Count;
        var notStartedRatio = (double)notStarted / tasks.Count;

        var score = 100.0;
        score -= Math.Min(40, delayed * 8);
        score -= Math.Min(20, notStartedRatio * 25);
        if (completion < 40) score -= 15;
        else if (completion < 60) score -= 8;
        else if (completion >= 85) score += 5;

        // Heavy overdue pressure
        var overdueDays = tasks
            .Where(t => t.IsDelayed && t.DueDate.HasValue)
            .Sum(t => Math.Max(0, (DateTime.UtcNow.Date - t.DueDate!.Value.UtcDateTime.Date).TotalDays));
        score -= Math.Min(15, overdueDays / 3.0);

        return (int)Math.Clamp(Math.Round(score), 0, 100);
    }

    private static string ScoreToRisk(int score, int delayed) =>
        score >= 75 && delayed <= 1 ? "Low"
        : score >= 50 ? "Medium"
        : "High";

    private static List<DelayPredictionItem> PredictDelays(IReadOnlyList<PlannerTaskItem> tasks, DateTime today)
    {
        var list = new List<DelayPredictionItem>();

        foreach (var t in tasks)
        {
            if (t.PercentComplete >= 100 || t.Status == "Completed") continue;

            var due = t.DueDate?.UtcDateTime.Date;
            int slip;
            string risk;
            string rationale;

            if (t.IsDelayed && due.HasValue)
            {
                slip = Math.Max(1, (int)(today - due.Value).TotalDays);
                risk = slip >= 7 ? "High" : "Medium";
                rationale = $"Already overdue by {slip} day(s) at {t.PercentComplete}% complete.";
            }
            else if (due.HasValue)
            {
                var daysLeft = (due.Value - today).TotalDays;
                if (daysLeft <= 7 && t.PercentComplete < 50)
                {
                    slip = Math.Max(1, (int)Math.Ceiling((50 - t.PercentComplete) / 10.0));
                    risk = daysLeft <= 3 && t.PercentComplete < 30 ? "High" : "Medium";
                    rationale = $"Due in {Math.Max(0, (int)daysLeft)} day(s) with only {t.PercentComplete}% done.";
                }
                else if (daysLeft <= 14 && t.PercentComplete < 25)
                {
                    slip = Math.Max(1, (int)Math.Ceiling((40 - t.PercentComplete) / 12.0));
                    risk = "Medium";
                    rationale = $"Two-week window, progress stalled at {t.PercentComplete}%.";
                }
                else if (daysLeft <= 21 && t.PercentComplete == 0)
                {
                    slip = 3;
                    risk = "Low";
                    rationale = "Not started with a near-term due date — watch for start lag.";
                }
                else
                {
                    continue;
                }
            }
            else if (t.PercentComplete is > 0 and < 40 && t.StartDate.HasValue
                     && (today - t.StartDate.Value.UtcDateTime.Date).TotalDays > 14)
            {
                slip = 5;
                risk = "Medium";
                rationale = "In progress >14 days without a due date and still under 40%.";
            }
            else
            {
                continue;
            }

            list.Add(new DelayPredictionItem
            {
                TaskId = t.Id,
                Title = t.Title,
                Assignee = t.AssignedUsers,
                BucketName = t.BucketName,
                DueDate = t.DueDate,
                PercentComplete = t.PercentComplete,
                IsAlreadyDelayed = t.IsDelayed,
                PredictedSlipDays = slip,
                RiskLevel = risk,
                Rationale = rationale,
                WhyExplanation = BuildDelayWhy(t, slip, risk, rationale)
            });
        }

        return list
            .OrderByDescending(p => p.RiskLevel == "High" ? 2 : p.RiskLevel == "Medium" ? 1 : 0)
            .ThenByDescending(p => p.PredictedSlipDays)
            .Take(25)
            .ToList();
    }

    private static List<ModuleAtRiskItem> AssessModules(IReadOnlyList<PlannerTaskItem> tasks)
    {
        return tasks
            .GroupBy(t => string.IsNullOrWhiteSpace(t.BucketName) ? "Uncategorized" : t.BucketName!)
            .Select(g =>
            {
                var list = g.ToList();
                var delayed = list.Count(t => t.IsDelayed);
                var incomplete = list.Count(t => t.PercentComplete < 100);
                var avg = list.Count == 0 ? 0 : list.Average(t => t.PercentComplete);
                var risk = delayed >= 2 || (delayed >= 1 && avg < 40) ? "High"
                    : delayed == 1 || (incomplete >= 3 && avg < 50) ? "Medium"
                    : "Low";
                var rec = risk == "High"
                    ? "Escalate owners; replan due dates or add capacity."
                    : risk == "Medium"
                        ? "Daily stand-up focus; clear blockers this week."
                        : "On track — keep current cadence.";

                return new ModuleAtRiskItem
                {
                    ModuleName = g.Key,
                    TaskCount = list.Count,
                    DelayedCount = delayed,
                    IncompleteCount = incomplete,
                    AvgPercentComplete = Math.Round(avg, 1),
                    RiskLevel = risk,
                    Recommendation = rec
                };
            })
            .Where(m => m.RiskLevel is "High" or "Medium" || m.DelayedCount > 0)
            .OrderByDescending(m => m.RiskLevel == "High" ? 2 : m.RiskLevel == "Medium" ? 1 : 0)
            .ThenByDescending(m => m.DelayedCount)
            .ToList();
    }

    private static List<StaffingRecommendation> RecommendStaffing(
        IReadOnlyList<PlannerTaskItem> tasks,
        IReadOnlyList<ModuleAtRiskItem> modules)
    {
        var recs = new List<StaffingRecommendation>();

        var byAssignee = new Dictionary<string, List<PlannerTaskItem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tasks)
        {
            var raw = string.IsNullOrWhiteSpace(t.AssignedUsers) ? "Unassigned" : t.AssignedUsers!;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var key = string.IsNullOrWhiteSpace(part) ? "Unassigned" : part;
                if (!byAssignee.TryGetValue(key, out var list))
                {
                    list = [];
                    byAssignee[key] = list;
                }

                list.Add(t);
            }
        }

        foreach (var (assignee, list) in byAssignee.OrderByDescending(kv => kv.Value.Count(t => t.IsDelayed)))
        {
            var delayed = list.Count(t => t.IsDelayed);
            if (assignee.Equals("Unassigned", StringComparison.OrdinalIgnoreCase) && delayed > 0)
            {
                recs.Add(new StaffingRecommendation
                {
                    Focus = "Unassigned delayed work",
                    Priority = "High",
                    Detail = $"Assign owners to {delayed} delayed task(s) with no assignee."
                });
                continue;
            }

            if (delayed >= 2)
            {
                recs.Add(new StaffingRecommendation
                {
                    Focus = assignee,
                    Priority = "High",
                    Detail = $"{delayed} delayed tasks — consider rebalancing load or pairing support."
                });
            }
            else if (list.Count >= 5 && list.Average(t => t.PercentComplete) < 40)
            {
                recs.Add(new StaffingRecommendation
                {
                    Focus = assignee,
                    Priority = "Medium",
                    Detail = $"{list.Count} open tasks at low average progress — protect focus time."
                });
            }
        }

        foreach (var m in modules.Where(x => x.RiskLevel == "High").Take(3))
        {
            recs.Add(new StaffingRecommendation
            {
                Focus = $"Module: {m.ModuleName}",
                Priority = "High",
                Detail = m.Recommendation
            });
        }

        if (recs.Count == 0)
        {
            recs.Add(new StaffingRecommendation
            {
                Focus = "Team",
                Priority = "Low",
                Detail = "No urgent staffing moves — maintain current allocation."
            });
        }

        return recs.Take(12).ToList();
    }

    private static HealthScoreExplanation BuildHealthExplanation(
        IReadOnlyList<PlannerTaskItem> tasks,
        int score,
        string risk)
    {
        var delayed = tasks.Count(t => t.IsDelayed);
        var completed = tasks.Count(t => t.PercentComplete >= 100 || t.Status == "Completed");
        var notStarted = tasks.Count(t => t.PercentComplete == 0 && t.Status != "Completed");
        var completion = tasks.Count == 0 ? 0 : 100.0 * completed / tasks.Count;
        var notStartedRatio = tasks.Count == 0 ? 0 : (double)notStarted / tasks.Count;
        var overdueDays = tasks
            .Where(t => t.IsDelayed && t.DueDate.HasValue)
            .Sum(t => Math.Max(0, (DateTime.UtcNow.Date - t.DueDate!.Value.UtcDateTime.Date).TotalDays));

        var factors = new List<ScoreFactor>();
        if (delayed > 0)
        {
            factors.Add(new ScoreFactor
            {
                Factor = "Delayed tasks",
                ImpactPoints = Math.Min(40, delayed * 8),
                Direction = "negative",
                Detail = $"{delayed} overdue task(s) reduce the score by up to 8 points each."
            });
        }

        if (notStartedRatio > 0.2)
        {
            factors.Add(new ScoreFactor
            {
                Factor = "Not started work",
                ImpactPoints = (int)Math.Min(20, notStartedRatio * 25),
                Direction = "negative",
                Detail = $"{notStarted} task(s) ({notStartedRatio:P0}) have not started."
            });
        }

        if (completion < 60)
        {
            factors.Add(new ScoreFactor
            {
                Factor = "Low completion rate",
                ImpactPoints = completion < 40 ? 15 : 8,
                Direction = "negative",
                Detail = $"Only {completion:0.#}% of tasks are complete."
            });
        }
        else if (completion >= 85)
        {
            factors.Add(new ScoreFactor
            {
                Factor = "Strong completion",
                ImpactPoints = 5,
                Direction = "positive",
                Detail = $"{completion:0.#}% completion adds a small boost."
            });
        }

        if (overdueDays > 0)
        {
            factors.Add(new ScoreFactor
            {
                Factor = "Overdue pressure",
                ImpactPoints = (int)Math.Min(15, overdueDays / 3.0),
                Direction = "negative",
                Detail = $"Cumulative {overdueDays:0} overdue day(s) across delayed tasks."
            });
        }

        if (factors.Count == 0)
        {
            factors.Add(new ScoreFactor
            {
                Factor = "Baseline",
                ImpactPoints = 0,
                Direction = "neutral",
                Detail = "No major negative drivers — score reflects steady progress."
            });
        }

        var narrative = $"Score {score}/100 ({risk}) driven by "
                        + string.Join("; ", factors.Take(3).Select(f => f.Factor.ToLowerInvariant()))
                        + ".";

        return new HealthScoreExplanation
        {
            HealthScore = score,
            RiskLevel = risk,
            Factors = factors,
            Narrative = narrative
        };
    }

    private static string BuildDelayWhy(
        PlannerTaskItem task,
        int slip,
        string risk,
        string rationale)
    {
        var owner = string.IsNullOrWhiteSpace(task.AssignedUsers) ? "Unassigned" : task.AssignedUsers!;
        var module = string.IsNullOrWhiteSpace(task.BucketName) ? "Uncategorized" : task.BucketName!;
        return risk switch
        {
            "High" => $"High delay risk because {owner} on '{module}' is {task.PercentComplete}% done "
                      + $"with ~{slip}d slip — {rationale}",
            "Medium" => $"Medium risk: progress vs due date on '{task.Title}' ({module}) — {rationale}",
            _ => $"Watch item: {rationale} Owner: {owner}."
        };
    }

    private static void AddParagraph(MainDocumentPart mainPart, string text, bool bold = false, int size = 20)
    {
        var runProps = new W.RunProperties(new W.FontSize { Val = size.ToString() });
        if (bold) runProps.AppendChild(new W.Bold());
        var para = new W.Paragraph(new W.Run(runProps, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        mainPart.Document.Body!.AppendChild(para);
    }
}
