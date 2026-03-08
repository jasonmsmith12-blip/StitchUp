namespace StitchUp.Contracts.Projects;

public class SaveProjectManifestRequestDto
{
    public ProjectManifestDto Manifest { get; set; } = new();

    public string? ChangeSummary { get; set; }
}
