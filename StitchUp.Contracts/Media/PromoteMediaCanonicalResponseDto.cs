namespace StitchUp.Contracts.Media;

public class PromoteMediaCanonicalResponseDto
{
    public Guid MediaId { get; set; }

    public string? CanonicalBlobPath { get; set; }

    public string? CanonicalContainer { get; set; }

    public string StorageState { get; set; } = string.Empty;

    public bool IsTemporary { get; set; }
}
