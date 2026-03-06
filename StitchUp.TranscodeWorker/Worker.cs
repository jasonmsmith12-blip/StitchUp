using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace StitchUp.TranscodeWorker;

public class Worker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan MessageVisibilityTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<Worker> _logger;
    private readonly QueueClient _queueClient;
    private readonly BlobContainerClient _rawContainer;
    private readonly BlobContainerClient _convertedContainer;
    private readonly TranscodeWorkerOptions _options;

    public Worker(
        ILogger<Worker> logger,
        QueueServiceClient queueServiceClient,
        BlobServiceClient blobServiceClient,
        TranscodeWorkerOptions options)
    {
        _logger = logger;
        _options = options;
        _queueClient = queueServiceClient.GetQueueClient(options.QueueName);
        _rawContainer = blobServiceClient.GetBlobContainerClient(options.RawContainer);
        _convertedContainer = blobServiceClient.GetBlobContainerClient(options.ConvertedContainer);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting transcode worker.");
        _logger.LogInformation(
            "Loaded options: account={StorageAccount}, raw={RawContainer}, converted={ConvertedContainer}, queue={QueueName}",
            _options.StorageAccount,
            _options.RawContainer,
            _options.ConvertedContainer,
            _options.QueueName);

        try
        {
            await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queue init failed");
            throw;
        }

        _logger.LogInformation("Queue ready: {QueueName}", _queueClient.Name);

        try
        {
            _logger.LogInformation("Polling queue {Queue}", _queueClient.Name);
            var response = await _queueClient.ReceiveMessagesAsync(
                maxMessages: 1,
                visibilityTimeout: MessageVisibilityTimeout,
                cancellationToken: stoppingToken);

            var message = response.Value.FirstOrDefault();
            if (message is null)
            {
                _logger.LogInformation("No queue messages available. Exiting.");
                return;
            }

            _logger.LogInformation("Dequeued message id={MessageId}", message.MessageId);
            await ProcessMessageAsync(message, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Cancellation requested. Exiting worker.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled transcode loop error.");
            throw;
        }

        _logger.LogInformation("Transcode worker stopped.");
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        var messageText = message.MessageText;
        if (!TryDeserializeMessage(messageText, out var request))
        {
            _logger.LogWarning("Invalid queue message JSON. Deleting messageId={MessageId}", message.MessageId);
            await DeleteMessageAsync(message, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.RawBlobName) || string.IsNullOrWhiteSpace(request.OutputBlobName))
        {
            _logger.LogWarning(
                "Queue message missing required fields rawBlobName/outputBlobName. Deleting messageId={MessageId}",
                message.MessageId);
            await DeleteMessageAsync(message, cancellationToken);
            return;
        }

        var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{Path.GetExtension(request.RawBlobName)}");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        try
        {
            _logger.LogInformation(
                "Processing blobs. rawBlobName={RawBlobName}, outputBlobName={OutputBlobName}",
                request.RawBlobName,
                request.OutputBlobName);

            _logger.LogInformation("Downloading raw blob: {BlobName}", request.RawBlobName);
            var rawBlobClient = _rawContainer.GetBlobClient(request.RawBlobName);
            await rawBlobClient.DownloadToAsync(tempInput, cancellationToken);
            _logger.LogInformation("Download complete: {BlobName}", request.RawBlobName);

            _logger.LogInformation("Running ffmpeg for {BlobName}", request.RawBlobName);
            await RunFfmpegAsync(tempInput, tempOutput, cancellationToken);

            _logger.LogInformation("Uploading converted blob: {BlobName}", request.OutputBlobName);
            var outputBlobClient = _convertedContainer.GetBlobClient(request.OutputBlobName);
            await outputBlobClient.UploadAsync(tempOutput, overwrite: true, cancellationToken: cancellationToken);
            _logger.LogInformation("Upload complete: {BlobName}", request.OutputBlobName);

            await DeleteMessageAsync(message, cancellationToken);
            _logger.LogInformation("Completed transcode. raw={RawBlob} output={OutputBlob}", request.RawBlobName, request.OutputBlobName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Message remains in queue and becomes visible again after visibility timeout.
            _logger.LogError(ex, "Failed processing messageId={MessageId}", message.MessageId);
        }
        finally
        {
            TryDeleteFile(tempInput);
            TryDeleteFile(tempOutput);
        }
    }

    private static bool TryDeserializeMessage(string messageText, out TranscodeRequestMessage request)
    {
        request = new TranscodeRequestMessage();

        if (string.IsNullOrWhiteSpace(messageText))
        {
            return false;
        }

        request = JsonSerializer.Deserialize<TranscodeRequestMessage>(messageText, JsonOptions) ?? new TranscodeRequestMessage();
        if (!string.IsNullOrWhiteSpace(request.RawBlobName) || !string.IsNullOrWhiteSpace(request.OutputBlobName))
        {
            return true;
        }

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(messageText));
            request = JsonSerializer.Deserialize<TranscodeRequestMessage>(decoded, JsonOptions) ?? new TranscodeRequestMessage();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunFfmpegAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo.FileName = "ffmpeg";
        process.StartInfo.Arguments =
            $"-y -i \"{inputPath}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -b:a 128k \"{outputPath}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ffmpeg process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            _logger.LogError(
                "ffmpeg failed. ExitCode={ExitCode}, stderr={StdErr}",
                process.ExitCode,
                stderr);
            throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}. stderr: {stderr}");
        }

        _logger.LogInformation("ffmpeg completed successfully. ExitCode={ExitCode}", process.ExitCode);

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            _logger.LogDebug("ffmpeg stdout: {Output}", stdout);
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            _logger.LogDebug("ffmpeg stderr: {Output}", stderr);
        }
    }

    private async Task DeleteMessageAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}

public sealed class TranscodeRequestMessage
{
    public string? RawBlobName { get; set; }

    public string? OutputBlobName { get; set; }
}
