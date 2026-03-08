namespace StitchUp.Domain.Entities.Server;

public class UserEntity
{
    public Guid UserId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<ProjectEntity> Projects { get; set; } = new List<ProjectEntity>();

    public ICollection<MediaEntity> Media { get; set; } = new List<MediaEntity>();

    public ICollection<ProjectManifestVersionEntity> ProjectManifestVersions { get; set; } = new List<ProjectManifestVersionEntity>();

    public ICollection<ProjectChangeProposalEntity> ProjectChangeProposals { get; set; } = new List<ProjectChangeProposalEntity>();

    public ICollection<ProjectChangeProposalEntity> ReviewedProjectChangeProposals { get; set; } = new List<ProjectChangeProposalEntity>();
}
