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

        async Task LogAndFlushAsync(string message)
        {
            var line = $"[{DateTime.UtcNow:O}] {message}";
            executionLog.AppendLine(line);
            _logger.LogInformation("{Message}", line);
            await UploadDiagnosticLogAsync(executionLog, stoppingToken);
        }

        await LogAndFlushAsync("worker start");
        await LogAndFlushAsync(
            $"Loaded options account={_options.StorageAccount}, raw={_options.RawContainer}, converted={_options.ConvertedContainer}, queue={_options.QueueName}");

        await UploadStartupDiagnosticAsync(stoppingToken);
        await LogAndFlushAsync("Uploaded startup diagnostics to diagnostics/startup.txt");

        try
        {
            await _queueClient.CreateIfNotExistsAsync(cancellationToken: stoppingToken);
            await LogAndFlushAsync($"Queue ready: {_queueClient.Name}");
        }
        catch (Exception ex)
        {
            executionLog.AppendLine($"[{DateTime.UtcNow:O}] Queue init failed");
            executionLog.AppendLine(ex.ToString());
            _logger.LogError(ex, "Queue init failed");
            await UploadDiagnosticLogAsync(executionLog, stoppingToken);
            throw;
        }

        try
        {
            await LogAndFlushAsync($"queue polling started: {_queueClient.Name}");
            var response = await _queueClient.ReceiveMessagesAsync(
                maxMessages: 1,
                visibilityTimeout: MessageVisibilityTimeout,
                cancellationToken: stoppingToken);

            var message = response.Value.FirstOrDefault();
            if (message is null)
            {
                await LogAndFlushAsync("No queue messages available. Exiting.");
                return;
            }

            await LogAndFlushAsync($"message dequeued id={message.MessageId}");
            await ProcessMessageAsync(message, executionLog, LogAndFlushAsync, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await LogAndFlushAsync("Cancellation requested. Exiting worker.");
        }
        catch (Exception ex)
        {
            executionLog.AppendLine($"[{DateTime.UtcNow:O}] Unhandled worker exception");
            executionLog.AppendLine(ex.ToString());
            _logger.LogError(ex, "Unhandled transcode loop error.");
            await UploadDiagnosticLogAsync(executionLog, stoppingToken);
            throw;
        }

        await LogAndFlushAsync("Transcode worker stopped.");
    }

    private async Task ProcessMessageAsync(
        QueueMessage message,
        StringBuilder executionLog,
        Func<string, Task> logAndFlushAsync,
        CancellationToken cancellationToken)
    {
        var messageText = message.MessageText;
        if (!TryDeserializeMessage(messageText, out var request))
        {
            _logger.LogWarning("Invalid queue message JSON. Deleting messageId={MessageId}", message.MessageId);
            await logAndFlushAsync($"Invalid queue message JSON. Deleting messageId={message.MessageId}");
            await DeleteMessageAsync(message, cancellationToken);
            return;
        }

        await logAndFlushAsync("message decoded");

        if (string.IsNullOrWhiteSpace(request.RawBlobName) || string.IsNullOrWhiteSpace(request.OutputBlobName))
        {
            _logger.LogWarning(
                "Queue message missing required fields rawBlobName/outputBlobName. Deleting messageId={MessageId}",
                message.MessageId);
            await logAndFlushAsync($"Queue message missing required fields. Deleting messageId={message.MessageId}");
            await DeleteMessageAsync(message, cancellationToken);
            return;
        }

        await logAndFlushAsync($"rawBlobName: {request.RawBlobName}");
        await logAndFlushAsync($"outputBlobName: {request.OutputBlobName}");

        var tempInput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{Path.GetExtension(request.RawBlobName)}");
        var tempOutput = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mp4");

        try
        {
            await logAndFlushAsync($"raw blob download started: {request.RawBlobName}");

            var rawBlobClient = _rawContainer.GetBlobClient(request.RawBlobName);
            await rawBlobClient.DownloadToAsync(tempInput, cancellationToken);
            await logAndFlushAsync($"raw blob download completed: {request.RawBlobName}");

            await logAndFlushAsync($"ffmpeg started args: -y -i \"{tempInput}\" -c:v libx264 -preset veryfast -crf 23 -c:a aac -b:a 128k \"{tempOutput}\"");
            var ffmpegResult = await RunFfmpegAsync(tempInput, tempOutput, cancellationToken);
            await logAndFlushAsync($"ffmpeg exited code={ffmpegResult.ExitCode}");

            if (!string.IsNullOrWhiteSpace(ffmpegResult.StandardOutput))
            {
                await logAndFlushAsync($"ffmpeg stdout: {ffmpegResult.StandardOutput}");
            }

            if (!string.IsNullOrWhiteSpace(ffmpegResult.StandardError))
            {
                await logAndFlushAsync($"ffmpeg stderr: {ffmpegResult.StandardError}");
            }

            await logAndFlushAsync($"output upload started: {request.OutputBlobName}");
            var outputBlobClient = _convertedContainer.GetBlobClient(request.OutputBlobName);
            await outputBlobClient.UploadAsync(tempOutput, overwrite: true, cancellationToken: cancellationToken);
            await logAndFlushAsync($"output upload completed: {request.OutputBlobName}");

            await logAndFlushAsync($"queue message delete started: {message.MessageId}");
            await DeleteMessageAsync(message, cancellationToken);
            await logAndFlushAsync($"queue message delete completed: {message.MessageId}");
            await logAndFlushAsync($"Completed transcode raw={request.RawBlobName} output={request.OutputBlobName}");

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
            await logAndFlushAsync($"exception: {ex}");
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

    private async Task UploadStartupDiagnosticAsync(CancellationToken cancellationToken)
    {
        var startupBlob = _convertedContainer.GetBlobClient("diagnostics/startup.txt");
        var content = new StringBuilder()
            .AppendLine($"timestampUtc={DateTime.UtcNow:O}")
            .AppendLine("worker started")
            .AppendLine($"storageAccount={_options.StorageAccount}")
            .AppendLine($"rawContainer={_options.RawContainer}")
            .AppendLine($"convertedContainer={_options.ConvertedContainer}")
            .AppendLine($"queueName={_options.QueueName}")
            .ToString();

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await startupBlob.UploadAsync(stream, overwrite: true, cancellationToken: cancellationToken);
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
