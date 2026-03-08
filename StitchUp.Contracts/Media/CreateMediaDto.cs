namespace StitchUp.Contracts.Media;

public class CreateMediaDto
{
    public Guid MediaId { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public string? OriginalBlobPath { get; set; }

    public bool WasCloudConverted { get; set; }

    public string? CloudConversionStatus { get; set; }
}
