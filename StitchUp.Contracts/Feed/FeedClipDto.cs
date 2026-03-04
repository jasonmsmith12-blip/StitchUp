namespace StitchUp.Contracts.Feed;

public class FeedClipDto
{
    public Guid MediaId { get; set; }

    public int OrderIndex { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public string ReadUrl { get; set; } = string.Empty;

    public bool WasCloudConverted { get; set; }

    public string CloudConversionStatus { get; set; } = string.Empty;
}
