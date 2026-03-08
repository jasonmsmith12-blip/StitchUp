namespace StitchUp.Contracts.Projects;

public class ProjectManifestChangeDto
{
    public string Type { get; set; } = string.Empty;

    public string? Message { get; set; }

    public Guid? MediaId { get; set; }

    public string? ProjectMediaKey { get; set; }

    public Guid? AddedByUserId { get; set; }

    public string? OldTitle { get; set; }

    public string? NewTitle { get; set; }

    public int? OldOrderIndex { get; set; }

    public int? NewOrderIndex { get; set; }

    public string? OldDescription { get; set; }

    public string? NewDescription { get; set; }
}
