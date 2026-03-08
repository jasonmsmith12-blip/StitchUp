namespace StitchUp.Contracts.Projects;

public class ProjectManifestMediaItemDto
{
    public Guid MediaId { get; set; }

    public Guid AddedByUserId { get; set; }

    public string? ProjectMediaKey { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public int OrderIndex { get; set; }
}
