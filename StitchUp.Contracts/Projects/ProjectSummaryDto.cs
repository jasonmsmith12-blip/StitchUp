namespace StitchUp.Contracts.Projects;

public class ProjectSummaryDto
{
    public Guid ProjectId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime UpdatedUtc { get; set; }

    public int ClipCount { get; set; }
}
