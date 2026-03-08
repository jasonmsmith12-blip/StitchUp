namespace StitchUp.Contracts.Projects;

public class ProjectManifestDiffDto
{
    public Guid ProjectId { get; set; }

    public int FromVersion { get; set; }

    public int ToVersion { get; set; }

    public List<ProjectManifestChangeDto> Changes { get; set; } = new();
}
