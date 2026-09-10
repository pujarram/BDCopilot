using System.Text;
using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BDCopilot.Infrastructure.Services;

/// <summary>Seeds a curated battlecards corpus for competitive positioning demos.</summary>
public interface IBattlecardsSeedService
{
    Task<int> SeedAsync(CancellationToken ct = default);

    Task<PublishBattleCardResult> PublishGeneratedAsync(
        PublishBattleCardRequest request,
        CancellationToken ct = default);
}

public sealed class BattlecardsSeedService : IBattlecardsSeedService
{
    private readonly BdCopilotDbContext _db;
    private readonly IDocumentSyncService _sync;
    private readonly ILogger<BattlecardsSeedService> _logger;

    public BattlecardsSeedService(
        BdCopilotDbContext db,
        IDocumentSyncService sync,
        ILogger<BattlecardsSeedService> logger)
    {
        _db = db;
        _sync = sync;
        _logger = logger;
    }

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        var cards = new (string FileName, string Title, string Body)[]
        {
            ("battlecard-incumbent-chatbot.docx", "Vs Incumbent Chatbot",
                "Where we win: grounded citations from SharePoint, ACL-aware answers, Planner delivery fusion. " +
                "Incumbent strengths: mature NLP for FAQs. Avoid claiming we replace all contact-center tooling."),
            ("battlecard-big-consult.docx", "Vs Big Consult AI Suite",
                "Where we win: faster time-to-value in Microsoft 365 tenants, Teams Adaptive Cards, lower TCO for BD teams. " +
                "Acknowledge their global delivery bench; counter with reusable RFP language and win/loss ranking."),
            ("battlecard-point-proposal.docx", "Vs Point Proposal Tools",
                "Where we win: end-to-end pursuit + delivery intelligence, not just slide generation. " +
                "Point tools excel at templates; we excel when corpus and Planner sync are live.")
        };

        var seeded = 0;
        foreach (var card in cards)
        {
            var existing = await _db.Documents.FirstOrDefaultAsync(
                d => d.GraphDriveId == CorpusSources.BattlecardsDriveId
                     && d.FileName == card.FileName, ct);
            if (existing is not null) continue;

            var doc = new Document
            {
                DocumentId = Guid.NewGuid(),
                FileName = card.FileName,
                FileType = "docx",
                Owner = "BD Enablement",
                CreatedDate = DateTimeOffset.UtcNow.AddMonths(-2),
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-14),
                TeamsChannel = "Battlecards",
                GraphDriveId = CorpusSources.BattlecardsDriveId,
                GraphDriveItemId = $"battle:{card.FileName}",
                SharePointUrl = $"https://contoso.sharepoint.com/sites/BD/Battlecards/{card.FileName}",
                IndexStatus = IndexStatus.Pending
            };
            _db.Documents.Add(doc);
            await _db.SaveChangesAsync(ct);

            await _sync.ChunkAndEmbedAsync(
                doc,
                new List<(string Text, string? Locator)>
                {
                    ($"{card.Title}\n\n{card.Body}", "p.1")
                },
                ct);
            seeded++;
        }

        _logger.LogInformation("Battlecards seed complete — {Count} new document(s)", seeded);
        return seeded;
    }

    public async Task<PublishBattleCardResult> PublishGeneratedAsync(
        PublishBattleCardRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request.Document);
        var title = string.IsNullOrWhiteSpace(request.Document.Title)
            ? "Generated Battle Card"
            : request.Document.Title.Trim();
        var safe = string.Join(
            "-",
            title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))
            .Replace(' ', '-');
        if (safe.Length > 80) safe = safe[..80];
        var fileName = $"battlecard-gen-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safe}.docx";

        var body = new StringBuilder();
        body.AppendLine(title);
        body.AppendLine();
        foreach (var section in request.Document.Sections.OrderBy(s => s.Order))
        {
            body.AppendLine($"## {section.Title}");
            body.AppendLine(section.Content);
            body.AppendLine();
        }

        var doc = new Document
        {
            DocumentId = Guid.NewGuid(),
            FileName = fileName,
            FileType = "docx",
            Owner = string.IsNullOrWhiteSpace(request.DisplayName) ? request.UserObjectId : request.DisplayName!,
            CreatedDate = DateTimeOffset.UtcNow,
            ModifiedDate = DateTimeOffset.UtcNow,
            TeamsChannel = "Battlecards",
            GraphDriveId = CorpusSources.BattlecardsDriveId,
            GraphDriveItemId = $"battle:gen:{request.Document.GenerationId:N}",
            SharePointUrl = $"https://contoso.sharepoint.com/sites/BD/Battlecards/{fileName}",
            IndexStatus = IndexStatus.Pending
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync(ct);

        await _sync.ChunkAndEmbedAsync(
            doc,
            new List<(string Text, string? Locator)>
            {
                (body.ToString(), "p.1")
            },
            ct);

        _logger.LogInformation(
            "Published generated battle card {GenerationId} as corpus document {DocumentId}",
            request.Document.GenerationId, doc.DocumentId);

        return new PublishBattleCardResult
        {
            DocumentId = doc.DocumentId,
            FileName = fileName,
            Title = title,
            Message = "Battle card indexed into the Battlecards corpus for future pursuits."
        };
    }
}
