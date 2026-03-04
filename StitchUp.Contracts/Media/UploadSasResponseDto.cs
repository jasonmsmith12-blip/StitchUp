namespace StitchUp.Contracts.Media;

public class UploadSasResponseDto
{
    public string BlobPath { get; set; } = string.Empty;

    public string UploadUrl { get; set; } = string.Empty;

    public DateTime ExpiresUtc { get; set; }
}
