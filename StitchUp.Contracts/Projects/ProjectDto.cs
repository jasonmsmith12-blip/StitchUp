namespace StitchUp.Contracts.Projects;

public class ProjectDto
{
    public Guid ProjectId { get; set; }

    public Guid AuthorUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime UpdatedUtc { get; set; }
}
