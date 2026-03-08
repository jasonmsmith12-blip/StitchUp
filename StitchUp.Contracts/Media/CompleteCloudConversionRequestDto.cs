namespace StitchUp.Contracts.Media;

public class CompleteCloudConversionRequestDto
{
    public string ConvertedBlobPath { get; set; } = string.Empty;

    public string ConvertedContainer { get; set; } = "stitchup-media-converted";
}
