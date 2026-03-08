namespace StitchUp.Contracts.Projects;

public class ProjectManifestConflictDto
{
    public Guid ProjectId { get; set; }

    public int RequestedVersion { get; set; }

    public int? BasedOnVersion { get; set; }

    public int? CurrentPublishedVersion { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}
