using BDCopilot.Core.Models;

namespace BDCopilot.Core.Interfaces;

public interface IGovernanceService
{
    ExportComplianceChecklist GetDefaultChecklist(Guid generationId, string documentTitle);
    Task<ComplianceChecklistResult> SubmitChecklistAsync(SubmitComplianceChecklistRequest request, CancellationToken ct = default);
    Task<bool> IsExportAllowedAsync(Guid generationId, CancellationToken ct = default);
    Task<GenerationSnapshot> SaveSnapshotAsync(SaveGenerationSnapshotRequest request, CancellationToken ct = default);
    Task<List<GenerationSnapshot>> ListSnapshotsAsync(Guid generationId, CancellationToken ct = default);
    Task<GenerationVersionDiff> DiffVersionsAsync(Guid generationId, int? fromVersion, int? toVersion, CancellationToken ct = default);
    Task LogAuditAsync(GovernanceAuditEvent evt, CancellationToken ct = default);
    Task<List<GovernanceAuditEvent>> QueryAuditAsync(GovernanceAuditQuery query, CancellationToken ct = default);
}

public interface IPipelineCapacityService
{
    Task<PipelineCapacityView> GetPipelineCapacityAsync(CancellationToken ct = default);
    Task<List<PursuitDeadlineAlert>> GetPursuitDeadlineAlertsAsync(int withinDays = 14, CancellationToken ct = default);
    Task<List<StaleDocumentAlert>> GetStaleDocumentAlertsAsync(CancellationToken ct = default);
}
