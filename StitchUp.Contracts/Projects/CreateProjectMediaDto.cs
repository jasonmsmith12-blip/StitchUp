namespace StitchUp.Contracts.Projects;

public class CreateProjectMediaDto
{
    public Guid ProjectId { get; set; }

    public Guid MediaId { get; set; }

    public int OrderIndex { get; set; }

    public string? ItemTitle { get; set; }

    public string? ItemDescription { get; set; }
}
