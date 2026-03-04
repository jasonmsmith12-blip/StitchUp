namespace StitchUp.Contracts.Media;

public class UploadSasRequestDto
{
    public Guid ProjectId { get; set; }

    public Guid MediaId { get; set; }

    public string FileName { get; set; } = string.Empty;
}
