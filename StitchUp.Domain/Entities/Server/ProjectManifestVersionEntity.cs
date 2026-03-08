namespace StitchUp.Domain.Entities.Server;

public class ProjectManifestVersionEntity
{
    public Guid ProjectManifestVersionId { get; set; }

    public Guid ProjectId { get; set; }

    public int VersionNumber { get; set; }

    public int? BasedOnVersionNumber { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public string ManifestJson { get; set; } = string.Empty;

    public string? ChangeSummary { get; set; }

    public bool IsPublished { get; set; }

    public ProjectEntity Project { get; set; } = null!;

    public UserEntity CreatedByUser { get; set; } = null!;

    public ICollection<ProjectChangeProposalEntity> ChangeProposals { get; set; } = new List<ProjectChangeProposalEntity>();
}
