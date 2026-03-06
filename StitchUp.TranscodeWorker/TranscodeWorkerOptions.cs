namespace StitchUp.TranscodeWorker;

public sealed class TranscodeWorkerOptions
{
    private const string StorageAccountKey = "STITCHUP_STORAGE_ACCOUNT";
    private const string RawContainerKey = "STITCHUP_RAW_CONTAINER";
    private const string ConvertedContainerKey = "STITCHUP_CONVERTED_CONTAINER";
    private const string QueueNameKey = "STITCHUP_QUEUE_NAME";

    public string StorageAccount { get; init; } = string.Empty;

    public string RawContainer { get; init; } = string.Empty;

    public string ConvertedContainer { get; init; } = string.Empty;

    public string QueueName { get; init; } = string.Empty;

    public static TranscodeWorkerOptions LoadFromEnvironment()
    {
        var options = new TranscodeWorkerOptions
        {
            StorageAccount = Environment.GetEnvironmentVariable(StorageAccountKey)?.Trim() ?? string.Empty,
            RawContainer = Environment.GetEnvironmentVariable(RawContainerKey)?.Trim() ?? string.Empty,
            ConvertedContainer = Environment.GetEnvironmentVariable(ConvertedContainerKey)?.Trim() ?? string.Empty,
            QueueName = Environment.GetEnvironmentVariable(QueueNameKey)?.Trim() ?? string.Empty
        };

        options.Validate();
        return options;
    }

    public void Validate()
    {
        RequireNotEmpty(StorageAccount, StorageAccountKey);
        RequireNotEmpty(RawContainer, RawContainerKey);
        RequireNotEmpty(ConvertedContainer, ConvertedContainerKey);
        RequireNotEmpty(QueueName, QueueNameKey);
    }

    private static void RequireNotEmpty(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required environment variable: {key}");
        }
    }
}
