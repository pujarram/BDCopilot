using BDCopilot.Core.Interfaces;
using BDCopilot.Core.Models;
using BDCopilot.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BDCopilot.Infrastructure.Services;

public class AccessAuditService : IAccessAuditService
{
    private readonly BdCopilotDbContext _db;

    public AccessAuditService(BdCopilotDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(AccessAuditRecord record, CancellationToken ct = default)
    {
        _db.AccessAuditRecords.Add(record);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AccessAuditRecord>> ListRecentAsync(int limit = 50, CancellationToken ct = default)
    {
        return await _db.AccessAuditRecords.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }
}
