using StitchUp.Contracts.Projects;

namespace StitchUp.Api.Services;

public interface IProjectManifestService
{
    Task<ProjectManifestDto?> GetLatestManifestAsync(Guid projectId, CancellationToken ct = default);

    Task<ProjectManifestVersionSummaryDto> SaveManifestVersionAsync(
        Guid projectId,
        Guid userId,
        ProjectManifestDto manifest,
        string? changeSummary,
        CancellationToken ct = default);

    Task<ProjectManifestDiffDto> GetManifestDiffAsync(
        Guid projectId,
        int fromVersion,
        int toVersion,
        CancellationToken ct = default);

    Task<ProjectManifestVersionSummaryDto> PublishManifestVersionAsync(Guid projectId, int version, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectManifestVersionSummaryDto>> GetManifestVersionsAsync(Guid projectId, int take = 20, CancellationToken ct = default);

    Task<IReadOnlyList<ProjectManifestVersionSummaryDto>> GetPendingProposalsAsync(Guid projectId, CancellationToken ct = default);

    Task<ProjectManifestVersionSummaryDto> AcceptProposalAsync(Guid projectId, int version, Guid reviewedByUserId, CancellationToken ct = default);

    Task<ProjectManifestVersionSummaryDto> RejectProposalAsync(Guid projectId, int version, Guid reviewedByUserId, CancellationToken ct = default);
}
