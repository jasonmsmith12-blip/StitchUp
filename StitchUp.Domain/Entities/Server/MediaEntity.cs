namespace StitchUp.Domain.Entities.Server;

public class MediaEntity
{
    public Guid MediaId { get; set; }

    public Guid AuthorUserId { get; set; }

    public string MediaType { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string BlobPath { get; set; } = string.Empty;

    public string? OriginalBlobPath { get; set; }

    public bool WasCloudConverted { get; set; }

    public string CloudConversionStatus { get; set; } = "NotRequested";

    public DateTime? CloudConvertedUtc { get; set; }

    public string? CloudConversionError { get; set; }

    public DateTime CreatedUtc { get; set; }

    public UserEntity AuthorUser { get; set; } = null!;

    public ICollection<ProjectMediaEntity> ProjectMedia { get; set; } = new List<ProjectMediaEntity>();

    public ICollection<MediaConversionAttemptEntity> ConversionAttempts { get; set; } = new List<MediaConversionAttemptEntity>();
}
