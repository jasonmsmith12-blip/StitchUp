namespace StitchUp.Contracts.Media;

public class ReadSasResponseDto
{
    public string Url { get; set; } = string.Empty;

    public DateTime ExpiresUtc { get; set; }
}
