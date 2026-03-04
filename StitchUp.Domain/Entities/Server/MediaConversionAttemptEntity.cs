namespace StitchUp.Domain.Entities.Server;

public class MediaConversionAttemptEntity
{
    public Guid ConversionAttemptId { get; set; }

    public Guid MediaId { get; set; }

    public DateTime AttemptedUtc { get; set; }

    public string AttemptSource { get; set; } = string.Empty;

    public string? InputFormatSummary { get; set; }

    public bool Succeeded { get; set; }

    public string? ErrorMessage { get; set; }

    public int? DurationMs { get; set; }

    public string? OutputBlobPath { get; set; }

    public MediaEntity Media { get; set; } = null!;
}
