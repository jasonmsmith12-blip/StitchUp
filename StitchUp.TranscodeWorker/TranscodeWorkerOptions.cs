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
        var storageAccount = Required(StorageAccountKey);
        var rawContainer = Required(RawContainerKey);
        var convertedContainer = Required(ConvertedContainerKey);
        var queueName = Required(QueueNameKey);

        return new TranscodeWorkerOptions
        {
            StorageAccount = storageAccount,
            RawContainer = rawContainer,
            ConvertedContainer = convertedContainer,
            QueueName = queueName
        };
    }

    private static string Required(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required environment variable: {key}");
        }

        return value.Trim();
    }
}
