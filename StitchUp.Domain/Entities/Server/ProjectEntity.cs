namespace StitchUp.Domain.Entities.Server;

public class ProjectEntity
{
    public Guid ProjectId { get; set; }

    public Guid AuthorUserId { get; set; }

    public int? CurrentPublishedVersionNumber { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public UserEntity AuthorUser { get; set; } = null!;

    public ProjectManifestVersionEntity? CurrentPublishedManifestVersion { get; set; }

    public ICollection<ProjectMediaEntity> ProjectMedia { get; set; } = new List<ProjectMediaEntity>();

    public ICollection<ProjectManifestVersionEntity> ManifestVersions { get; set; } = new List<ProjectManifestVersionEntity>();

    public ICollection<ProjectChangeProposalEntity> ChangeProposals { get; set; } = new List<ProjectChangeProposalEntity>();
}
