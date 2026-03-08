namespace StitchUp.Contracts.Projects;

public class ProjectManifestDto
{
    public Guid ProjectId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public int Version { get; set; }

    public int? BasedOnVersion { get; set; }

    public List<ProjectManifestMediaItemDto> MediaItems { get; set; } = new();
}
