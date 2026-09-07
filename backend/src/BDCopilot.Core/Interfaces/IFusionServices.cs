namespace BDCopilot.Core.Interfaces;

using BDCopilot.Core.Models;

public interface IGraphUserDisplayNameService
{
    Task<IReadOnlyDictionary<string, string>> ResolveDisplayNamesAsync(
        IEnumerable<string> objectIds,
        CancellationToken ct = default);
}

public interface ICapacityImportService
{
    Task<CapacityImportResult> ImportCsvAsync(Stream csvStream, CancellationToken ct = default);
}

public interface IPartnerOnboardingService
{
    Task<PartnerOnboardingProfile> GetProfileAsync(CancellationToken ct = default);
    Task<PartnerOnboardingProfile> SaveProfileAsync(PartnerOnboardingProfile profile, CancellationToken ct = default);
    Task<string> GenerateTeamsManifestAsync(CancellationToken ct = default);
}
