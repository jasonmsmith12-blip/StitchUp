namespace StitchUp.Contracts.Media;

public class CompleteCloudConversionResponseDto
{
    public Guid MediaId { get; set; }

    public string ConvertedBlobPath { get; set; } = string.Empty;

    public string ConvertedContainer { get; set; } = string.Empty;

    public string StorageState { get; set; } = string.Empty;

    public bool IsTemporary { get; set; }

    public DateTime? TemporaryExpiresUtc { get; set; }
}
