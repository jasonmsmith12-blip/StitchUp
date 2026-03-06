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
        var executionLog = new StringBuilder();

        void LogLine(string message)
        {
            var line = $"[{DateTime.UtcNow:O}] {message}";
            executionLog.AppendLine(line);
            _logger.LogInformation("{Message}", line);
        }

        LogLine("Worker start");
        LogLine(
            $"Loaded options account={_options.StorageAccount}, raw={_options.RawContainer}, converted={_options.ConvertedContainer}, queue={_options.QueueName}");

        try
        {
            await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
            LogLine($"Queue ready: {_queueClient.Name}");
        }
        catch (Exception ex)
        {
            executionLog.AppendLine($"[{DateTime.UtcNow:O}] Queue init failed");
            executionLog.AppendLine(ex.ToString());
            _logger.LogError(ex, "Queue init failed");
            throw;
        }

        try
        {
            LogLine($"Polling queue {_queueClient.Name}");
            var response = await _queueClient.ReceiveMessagesAsync(
                maxMessages: 1,
                visibilityTimeout: MessageVisibilityTimeout,
                cancellationToken: stoppingToken);

            var message = response.Value.FirstOrDefault();
            if (message is null)
            {
                LogLine("No queue messages available. Exiting.");
                return;
            }

            LogLine($"Dequeue result: message found");
            LogLine($"Message id: {message.MessageId}");
            await ProcessMessageAsync(message, executionLog, LogLine, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            LogLine("Cancellation requested. Exiting worker.");
        }
        catch (Exception ex)
        {
            executionLog.AppendLine($"[{DateTime.UtcNow:O}] Unhandled worker exception");
            executionLog.AppendLine(ex.ToString());
            _logger.LogError(ex, "Unhandled transcode loop error.");
            throw;
        }

        LogLine("Transcode worker stopped.");
    }

    private async Task ProcessMessageAsync(
        QueueMessage message,
        StringBuilder executionLog,
        Action<string> logLine,
        CancellationToken cancellationToken)
    {
        var messageText = message.MessageText;
        if (!TryDeserializeMessage(messageText, out var request))
        {
            _logger.LogWarning("Invalid queue message JSON. Deleting messageId={MessageId}", message.MessageId);
            logLine($"Invalid queue message JSON. Deleting messageId={message.MessageId}");
            await DeleteMessageAsync(message, cancellationToken);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.RawBlobName) || string.IsNullOrWhiteSpace(request.OutputBlobName))
        {
            _logger.LogWarning(
                "Queue message missing required fields rawBlobName/outputBlobName. Deleting messageId={MessageId}",
                message.MessageId);
            logLine($"Queue message missing required fields. Deleting messageId={message.MessageId}");
            await DeleteMessageAsync(message, cancellationToken);
            return;
        }

        logLine($"rawBlobName: {request.RawBlobName}");
        logLine($"outputBlobName: {request.OutputBlobName}");

        var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{Path.GetExtension(request.RawBlobName)}");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        try
        {
            logLine($"Raw blob download start: {request.RawBlobName}");

            var rawBlobClient = _rawContainer.GetBlobClient(request.RawBlobName);
            await rawBlobClient.DownloadToAsync(tempInput, cancellationToken);
            logLine($"Raw blob download end: {request.RawBlobName}");

            logLine($"ffmpeg command start: ffmpeg -y -i \"{tempInput}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -b:a 128k \"{tempOutput}\"");
            var ffmpegResult = await RunFfmpegAsync(tempInput, tempOutput, cancellationToken);
            logLine($"ffmpeg exit code: {ffmpegResult.ExitCode}");

            if (!string.IsNullOrWhiteSpace(ffmpegResult.StandardOutput))
            {
                logLine($"ffmpeg stdout: {ffmpegResult.StandardOutput}");
            }

            if (!string.IsNullOrWhiteSpace(ffmpegResult.StandardError))
            {
                logLine($"ffmpeg stderr: {ffmpegResult.StandardError}");
            }

            logLine($"Upload start: {request.OutputBlobName}");
            var outputBlobClient = _convertedContainer.GetBlobClient(request.OutputBlobName);
            await outputBlobClient.UploadAsync(tempOutput, overwrite: true, cancellationToken: cancellationToken);
            logLine($"Upload end: {request.OutputBlobName}");

            logLine($"Delete message start: {message.MessageId}");
            await DeleteMessageAsync(message, cancellationToken);
            logLine($"Delete message end: {message.MessageId}");
            logLine($"Completed transcode raw={request.RawBlobName} output={request.OutputBlobName}");

            await UploadDiagnosticLogAsync(executionLog, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Message remains in queue and becomes visible again after visibility timeout.
            _logger.LogError(ex, "Failed processing messageId={MessageId}", message.MessageId);
            executionLog.AppendLine($"[{DateTime.UtcNow:O}] Processing failed for messageId={message.MessageId}");
            executionLog.AppendLine(ex.ToString());
            try
            {
                await UploadDiagnosticLogAsync(executionLog, cancellationToken);
            }
            catch (Exception uploadEx)
            {
                _logger.LogError(uploadEx, "Failed to upload diagnostics log blob.");
            }

            throw;
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

    private async Task<FfmpegResult> RunFfmpegAsync(string inputPath, string outputPath, CancellationToken cancellationToken)
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
        return new FfmpegResult(process.ExitCode, stdout, stderr);
    }

    private async Task UploadDiagnosticLogAsync(StringBuilder executionLog, CancellationToken cancellationToken)
    {
        var logBlob = _convertedContainer.GetBlobClient("diagnostics/last-worker-log.txt");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(executionLog.ToString()));
        await logBlob.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
        _logger.LogInformation("Uploaded diagnostic execution log to diagnostics/last-worker-log.txt");
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

public sealed record FfmpegResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class TranscodeRequestMessage
{
    public string? RawBlobName { get; set; }

    public string? OutputBlobName { get; set; }
}
