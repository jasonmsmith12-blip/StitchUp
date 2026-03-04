namespace StitchUp.Contracts.Projects;

public class ProjectClipDto
{
    public Guid ProjectMediaId { get; set; }

    public Guid ProjectId { get; set; }

    public int OrderIndex { get; set; }

    public string? ItemTitle { get; set; }

    public string? ItemDescription { get; set; }

    public DateTime AddedUtc { get; set; }

    public Guid MediaId { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
