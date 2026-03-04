namespace StitchUp.Contracts.Media;

public class ReadSasBatchRequestDto
{
    public Guid ProjectId { get; set; }

    public List<Guid> MediaIds { get; set; } = new();
}
