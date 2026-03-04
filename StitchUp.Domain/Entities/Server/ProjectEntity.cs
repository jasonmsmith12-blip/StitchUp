namespace StitchUp.Domain.Entities.Server;

public class ProjectEntity
{
    public Guid ProjectId { get; set; }

    public Guid AuthorUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public UserEntity AuthorUser { get; set; } = null!;

    public ICollection<ProjectMediaEntity> ProjectMedia { get; set; } = new List<ProjectMediaEntity>();
}
