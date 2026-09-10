using BDCopilot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

/// <summary>EF-translatable corpus filters (static helpers on <see cref="CorpusSources"/> are not translatable).</summary>
public static class CorpusSourceQuery
{
    public static IQueryable<Document> ApplyCorpusFilter(this IQueryable<Document> query, string corpusSource) =>
        corpusSource switch
        {
            CorpusSources.Local => query.Where(d => d.GraphDriveId == CorpusSources.LocalDriveId),
            CorpusSources.Online => query.Where(d =>
                d.GraphDriveId != CorpusSources.LocalDriveId
                && d.GraphDriveId != CorpusSources.PlannerDriveId
                && d.GraphDriveId != CorpusSources.BattlecardsDriveId),
            CorpusSources.Planner => query.Where(d => d.GraphDriveId == CorpusSources.PlannerDriveId),
            CorpusSources.Battlecards => query.Where(d => d.GraphDriveId == CorpusSources.BattlecardsDriveId),
            _ => query
        };

    public static IQueryable<DocumentChunk> ApplyCorpusFilter(this IQueryable<DocumentChunk> query, string corpusSource) =>
        corpusSource switch
        {
            CorpusSources.Local => query.Where(c => c.Document!.GraphDriveId == CorpusSources.LocalDriveId),
            CorpusSources.Online => query.Where(c =>
                c.Document!.GraphDriveId != CorpusSources.LocalDriveId
                && c.Document!.GraphDriveId != CorpusSources.PlannerDriveId
                && c.Document!.GraphDriveId != CorpusSources.BattlecardsDriveId),
            CorpusSources.Planner => query.Where(c => c.Document!.GraphDriveId == CorpusSources.PlannerDriveId),
            CorpusSources.Battlecards => query.Where(c => c.Document!.GraphDriveId == CorpusSources.BattlecardsDriveId),
            _ => query
        };
}
