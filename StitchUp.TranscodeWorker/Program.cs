using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Microsoft.Extensions.Hosting;
using StitchUp.TranscodeWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<HostOptions>(o =>
    o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);

var options = TranscodeWorkerOptions.LoadFromEnvironment();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    return new BlobServiceClient(new Uri($"https://{options.StorageAccount}.blob.core.windows.net"), credential);
});
builder.Services.AddSingleton(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    return new QueueServiceClient(
        new Uri($"https://{options.StorageAccount}.queue.core.windows.net"),
        credential);
});
builder.Services.AddHostedService<Worker>();

var app = builder.Build();
app.Run();
