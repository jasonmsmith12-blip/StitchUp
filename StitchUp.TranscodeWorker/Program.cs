using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.Run();

public class Worker : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<Worker> _logger;

    public Worker(IConfiguration config, ILogger<Worker> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var queueClient = new QueueClient(
            _config["StorageConnectionString"],
            "transcode-requests");

        await queueClient.CreateIfNotExistsAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            var message = await queueClient.ReceiveMessageAsync();

            if (message.Value == null)
            {
                await Task.Delay(5000, stoppingToken);
                continue;
            }

            var payload = JsonSerializer.Deserialize<TranscodeRequest>(
                Encoding.UTF8.GetString(Convert.FromBase64String(message.Value.MessageText)));

            _logger.LogInformation("Processing media {MediaId}", payload.MediaId);

            var rawBlob = new BlobClient(
                _config["StorageConnectionString"],
                "stitchup-media-raw",
                payload.RawBlobName);

            var convertedBlob = new BlobClient(
                _config["StorageConnectionString"],
                "stitchup-media-converted",
                payload.ConvertedBlobName);

            var tempInput = Path.GetTempFileName();
            var tempOutput = Path.GetTempFileName() + ".mp4";

            await rawBlob.DownloadToAsync(tempInput);

            var ffmpeg = new System.Diagnostics.Process();
            ffmpeg.StartInfo.FileName = "ffmpeg";
            ffmpeg.StartInfo.Arguments =
                $"-i \"{tempInput}\" -vf scale=1280:-2 -c:v libx264 -preset fast -crf 23 \"{tempOutput}\"";
            ffmpeg.StartInfo.RedirectStandardOutput = true;
            ffmpeg.StartInfo.RedirectStandardError = true;

            ffmpeg.Start();
            await ffmpeg.WaitForExitAsync();

            await convertedBlob.UploadAsync(tempOutput, overwrite: true);

            await queueClient.DeleteMessageAsync(message.Value.MessageId, message.Value.PopReceipt);

            _logger.LogInformation("Completed media {MediaId}", payload.MediaId);
        }
    }
}

public record TranscodeRequest(Guid MediaId, string RawBlobName, string ConvertedBlobName);