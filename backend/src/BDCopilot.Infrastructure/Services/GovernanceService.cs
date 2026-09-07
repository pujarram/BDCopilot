using System.Text.Json;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

public sealed class GovernanceService : IGovernanceService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly BdCopilotDbContext _db;
    private readonly ILogger<GovernanceService> _logger;

    public GovernanceService(BdCopilotDbContext db, ILogger<GovernanceService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public ExportComplianceChecklist GetDefaultChecklist(Guid generationId, string documentTitle)
    {
        var items = DefaultChecklistItems();
        return new ExportComplianceChecklist
        {
            GenerationId = generationId,
            DocumentTitle = documentTitle,
            Items = items,
            AllRequiredComplete = false,
            Guidance = "Complete all required checks before export. Flag confidential clauses and pricing sensitivity explicitly."
        };
    }

    public async Task<ComplianceChecklistResult> SubmitChecklistAsync(
        SubmitComplianceChecklistRequest request,
        CancellationToken ct = default)
    {
        var required = request.Items.Where(i => i.Required).ToList();
        var checkedRequired = required.Count(i => i.Checked);
        var ready = required.Count > 0 && checkedRequired == required.Count;

        var existing = await _db.ExportComplianceChecklists
            .FirstOrDefaultAsync(c => c.GenerationId == request.GenerationId, ct);

        var json = JsonSerializer.Serialize(request.Items, JsonOpts);
        if (existing is null)
        {
            _db.ExportComplianceChecklists.Add(new ExportComplianceChecklistRecord
            {
                GenerationId = request.GenerationId,
                ItemsJson = json,
                SubmittedByUserObjectId = request.UserObjectId,
                ReadyForExport = ready
            });
        }
        else
        {
            existing.ItemsJson = json;
            existing.SubmittedByUserObjectId = request.UserObjectId;
            existing.ReadyForExport = ready;
            existing.SubmittedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        await LogAuditAsync(new GovernanceAuditEvent
        {
            GenerationId = request.GenerationId,
            UserObjectId = request.UserObjectId,
            EventType = "ComplianceCheck",
            ResourceType = "Generation",
            ResourceId = request.GenerationId.ToString(),
            Outcome = ready ? "Pass" : "Incomplete",
            Detail = $"Checklist {checkedRequired}/{required.Count} required items complete."
        }, ct);

        return new ComplianceChecklistResult
        {
            GenerationId = request.GenerationId,
            ReadyForExport = ready,
            RequiredChecked = checkedRequired,
            RequiredTotal = required.Count,
            Message = ready
                ? "Compliance checklist complete — export is allowed."
                : $"Complete {required.Count - checkedRequired} remaining required item(s) before export."
        };
    }

    public async Task<bool> IsExportAllowedAsync(Guid generationId, CancellationToken ct = default)
    {
        var row = await _db.ExportComplianceChecklists
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.GenerationId == generationId, ct);
        return row?.ReadyForExport == true;
    }

    public async Task<GenerationSnapshot> SaveSnapshotAsync(
        SaveGenerationSnapshotRequest request,
        CancellationToken ct = default)
    {
        var lastVersion = await _db.GenerationSnapshots
            .Where(s => s.GenerationId == request.GenerationId)
            .MaxAsync(s => (int?)s.VersionNumber, ct) ?? 0;

        var version = lastVersion + 1;
        var sectionsJson = JsonSerializer.Serialize(request.Document.Sections, JsonOpts);
        var changeSummary = lastVersion == 0
            ? "Initial draft snapshot."
            : $"Version {version} saved for review.";

        var row = new GenerationSnapshotRecord
        {
            GenerationId = request.GenerationId,
            VersionNumber = version,
            DocumentTitle = request.Document.Title,
            SectionsJson = sectionsJson,
            CreatedByUserObjectId = request.UserObjectId,
            CreatedByDisplayName = request.DisplayName,
            ChangeSummary = changeSummary
        };

        _db.GenerationSnapshots.Add(row);
        await _db.SaveChangesAsync(ct);

        await LogAuditAsync(new GovernanceAuditEvent
        {
            GenerationId = request.GenerationId,
            UserObjectId = request.UserObjectId,
            UserDisplayName = request.DisplayName,
            EventType = "VersionSave",
            ResourceType = "GenerationSnapshot",
            ResourceId = row.Id.ToString(),
            Outcome = "Saved",
            Detail = $"Saved version {version} of '{request.Document.Title}'."
        }, ct);

        return MapSnapshot(row);
    }

    public async Task<List<GenerationSnapshot>> ListSnapshotsAsync(Guid generationId, CancellationToken ct = default)
    {
        var rows = await _db.GenerationSnapshots.AsNoTracking()
            .Where(s => s.GenerationId == generationId)
            .OrderByDescending(s => s.VersionNumber)
            .ToListAsync(ct);
        return rows.Select(MapSnapshot).ToList();
    }

    public async Task<GenerationVersionDiff> DiffVersionsAsync(
        Guid generationId,
        int? fromVersion,
        int? toVersion,
        CancellationToken ct = default)
    {
        var snapshots = await _db.GenerationSnapshots.AsNoTracking()
            .Where(s => s.GenerationId == generationId)
            .OrderBy(s => s.VersionNumber)
            .ToListAsync(ct);

        if (snapshots.Count == 0)
        {
            return new GenerationVersionDiff
            {
                GenerationId = generationId,
                Summary = "No saved versions yet — save a snapshot to enable diffing."
            };
        }

        var to = snapshots.FirstOrDefault(s => s.VersionNumber == toVersion)
                 ?? snapshots[^1];
        var from = fromVersion.HasValue
            ? snapshots.FirstOrDefault(s => s.VersionNumber == fromVersion.Value)
            : snapshots.Count >= 2
                ? snapshots[^2]
                : snapshots[0];

        if (from is null)
        {
            from = snapshots[0];
        }

        var fromSections = DeserializeSections(from.SectionsJson);
        var toSections = DeserializeSections(to.SectionsJson);
        var diffs = BuildSectionDiff(fromSections, toSections);

        var modified = diffs.Count(d => d.ChangeKind == "Modified");
        var added = diffs.Count(d => d.ChangeKind == "Added");
        var removed = diffs.Count(d => d.ChangeKind == "Removed");

        return new GenerationVersionDiff
        {
            GenerationId = generationId,
            FromVersion = from.VersionNumber,
            ToVersion = to.VersionNumber,
            Sections = diffs,
            Summary = modified + added + removed == 0
                ? $"No changes between v{from.VersionNumber} and v{to.VersionNumber}."
                : $"v{from.VersionNumber} → v{to.VersionNumber}: {modified} modified, {added} added, {removed} removed section(s)."
        };
    }

    public async Task LogAuditAsync(GovernanceAuditEvent evt, CancellationToken ct = default)
    {
        _db.GovernanceAuditRecords.Add(new GovernanceAuditRecord
        {
            Id = evt.Id,
            GenerationId = evt.GenerationId,
            UserObjectId = evt.UserObjectId,
            UserDisplayName = evt.UserDisplayName,
            EventType = evt.EventType,
            ResourceType = evt.ResourceType,
            ResourceId = evt.ResourceId,
            Outcome = evt.Outcome,
            Detail = evt.Detail,
            ComplianceFramework = evt.ComplianceFramework,
            CreatedAt = evt.CreatedAt
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Governance audit {EventType} gen={GenerationId} user={UserObjectId} outcome={Outcome}",
            evt.EventType, evt.GenerationId, evt.UserObjectId, evt.Outcome);
    }

    public async Task<List<GovernanceAuditEvent>> QueryAuditAsync(
        GovernanceAuditQuery query,
        CancellationToken ct = default)
    {
        var q = _db.GovernanceAuditRecords.AsNoTracking().AsQueryable();
        if (query.GenerationId.HasValue)
        {
            q = q.Where(e => e.GenerationId == query.GenerationId);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            q = q.Where(e => e.EventType == query.EventType);
        }

        var rows = await q
            .OrderByDescending(e => e.CreatedAt)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .ToListAsync(ct);

        return rows.Select(r => new GovernanceAuditEvent
        {
            Id = r.Id,
            GenerationId = r.GenerationId,
            UserObjectId = r.UserObjectId,
            UserDisplayName = r.UserDisplayName,
            EventType = r.EventType,
            ResourceType = r.ResourceType,
            ResourceId = r.ResourceId,
            Outcome = r.Outcome,
            Detail = r.Detail,
            ComplianceFramework = r.ComplianceFramework,
            CreatedAt = r.CreatedAt
        }).ToList();
    }

    private static List<ComplianceChecklistItem> DefaultChecklistItems() =>
    [
        new() { Id = "confidential", Label = "Confidential clauses reviewed and marked or redacted", Category = "Confidentiality", Required = true },
        new() { Id = "pricing", Label = "Pricing / rate sensitivity checked — no unapproved figures", Category = "Pricing", Required = true },
        new() { Id = "nda", Label = "Client NDA and data-handling obligations referenced", Category = "Legal", Required = true },
        new() { Id = "audience", Label = "Export audience and distribution list confirmed", Category = "Governance", Required = true },
        new() { Id = "redaction", Label = "Third-party / competitor references sanitized where required", Category = "Redaction", Required = true },
        new() { Id = "notes", Label = "Reviewer notes captured for audit (optional)", Category = "Audit", Required = false }
    ];

    private static GenerationSnapshot MapSnapshot(GenerationSnapshotRecord row) =>
        new()
        {
            Id = row.Id,
            GenerationId = row.GenerationId,
            VersionNumber = row.VersionNumber,
            DocumentTitle = row.DocumentTitle,
            SectionsJson = row.SectionsJson,
            CreatedByUserObjectId = row.CreatedByUserObjectId,
            CreatedByDisplayName = row.CreatedByDisplayName,
            CreatedAt = row.CreatedAt,
            ChangeSummary = row.ChangeSummary
        };

    private static List<GeneratedSection> DeserializeSections(string json) =>
        JsonSerializer.Deserialize<List<GeneratedSection>>(json, JsonOpts) ?? [];

    private static List<SectionDiffItem> BuildSectionDiff(
        IReadOnlyList<GeneratedSection> from,
        IReadOnlyList<GeneratedSection> to)
    {
        var fromByTitle = from.ToDictionary(s => s.Title, StringComparer.OrdinalIgnoreCase);
        var toByTitle = to.ToDictionary(s => s.Title, StringComparer.OrdinalIgnoreCase);
        var allTitles = fromByTitle.Keys.Union(toByTitle.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(t => t);
        var diffs = new List<SectionDiffItem>();

        foreach (var title in allTitles)
        {
            fromByTitle.TryGetValue(title, out var before);
            toByTitle.TryGetValue(title, out var after);

            if (before is null && after is not null)
            {
                diffs.Add(new SectionDiffItem
                {
                    Title = title,
                    ChangeKind = "Added",
                    AfterExcerpt = Excerpt(after.Content),
                    LinesAdded = CountLines(after.Content)
                });
            }
            else if (before is not null && after is null)
            {
                diffs.Add(new SectionDiffItem
                {
                    Title = title,
                    ChangeKind = "Removed",
                    BeforeExcerpt = Excerpt(before.Content),
                    LinesRemoved = CountLines(before.Content)
                });
            }
            else if (before is not null && after is not null)
            {
                if (string.Equals(Normalize(before.Content), Normalize(after.Content), StringComparison.Ordinal))
                {
                    diffs.Add(new SectionDiffItem { Title = title, ChangeKind = "Unchanged" });
                }
                else
                {
                    var (added, removed) = LineDelta(before.Content, after.Content);
                    diffs.Add(new SectionDiffItem
                    {
                        Title = title,
                        ChangeKind = "Modified",
                        BeforeExcerpt = Excerpt(before.Content),
                        AfterExcerpt = Excerpt(after.Content),
                        LinesAdded = added,
                        LinesRemoved = removed
                    });
                }
            }
        }

        return diffs.Where(d => d.ChangeKind != "Unchanged").ToList();
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();
    private static int CountLines(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split('\n').Length;

    private static string Excerpt(string text)
    {
        var t = text.Replace("\r\n", " ").Replace('\n', ' ').Trim();
        return t.Length <= 180 ? t : t[..180].TrimEnd() + "…";
    }

    private static (int added, int removed) LineDelta(string before, string after)
    {
        var b = before.Split('\n', StringSplitOptions.TrimEntries);
        var a = after.Split('\n', StringSplitOptions.TrimEntries);
        var bSet = new HashSet<string>(b);
        var aSet = new HashSet<string>(a);
        return (a.Count(l => !bSet.Contains(l)), b.Count(l => !aSet.Contains(l)));
    }
}
