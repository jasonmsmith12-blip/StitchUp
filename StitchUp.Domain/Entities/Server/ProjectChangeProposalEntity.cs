namespace StitchUp.Domain.Entities.Server;

public class ProjectChangeProposalEntity
{
    public Guid ProjectChangeProposalId { get; set; }

    public Guid ProjectId { get; set; }

    public int BaseVersionNumber { get; set; }

    public int ProposedVersionNumber { get; set; }

    public Guid ProposedByUserId { get; set; }

    public Guid ProjectManifestVersionId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ChangeSummary { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? ReviewedUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public ProjectEntity Project { get; set; } = null!;

    public UserEntity ProposedByUser { get; set; } = null!;

    public UserEntity? ReviewedByUser { get; set; }

    public ProjectManifestVersionEntity ProjectManifestVersion { get; set; } = null!;
}
