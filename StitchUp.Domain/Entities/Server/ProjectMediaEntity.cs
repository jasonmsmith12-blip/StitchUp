namespace StitchUp.Domain.Entities.Server;

public class ProjectMediaEntity
{
    public Guid ProjectMediaId { get; set; }

    public Guid ProjectId { get; set; }

    public Guid MediaId { get; set; }

    public int OrderIndex { get; set; }

    public string? ItemTitle { get; set; }

    public string? ItemDescription { get; set; }

    public DateTime AddedUtc { get; set; }

    public ProjectEntity Project { get; set; } = null!;

    public MediaEntity Media { get; set; } = null!;
}
