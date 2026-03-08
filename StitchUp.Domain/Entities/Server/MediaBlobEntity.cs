namespace StitchUp.Domain.Entities.Server;

public class MediaBlobEntity
{
    public Guid MediaBlobId { get; set; }

    public Guid MediaId { get; set; }

    public string BlobRole { get; set; } = string.Empty;

    public string ContainerName { get; set; } = string.Empty;

    public string BlobPath { get; set; } = string.Empty;

    public bool IsTemporary { get; set; }

    public DateTime? TemporaryExpiresUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? DeletedUtc { get; set; }

    public MediaEntity Media { get; set; } = null!;
}
