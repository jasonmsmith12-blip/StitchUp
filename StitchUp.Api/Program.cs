using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StitchUp.Api.Auth;
using StitchUp.Api.Extensions;
using StitchUp.Api.Middleware;
using StitchUp.Api.Startup;
using StitchUp.Contracts.Feed;
using StitchUp.Contracts.Media;
using StitchUp.Contracts.Projects;
using StitchUp.Domain.Entities.Server;
using StitchUp.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<XUserIdHeaderOperationFilter>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HeaderCurrentUser>();
builder.Services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();

var stitchUpSqlConnectionString = builder.Configuration.GetConnectionString("StitchUpSql")
    ?? throw new InvalidOperationException("Missing connection string: ConnectionStrings:StitchUpSql");

builder.Services.Configure<AzureStorageSettings>(builder.Configuration.GetSection("AzureStorage"));

var azureStorageSettings = builder.Configuration.GetSection("AzureStorage").Get<AzureStorageSettings>() ?? new AzureStorageSettings();

// Example user-secrets:
// dotnet user-secrets set "AzureStorage:ConnectionString" "<real connection string>"
// dotnet user-secrets set "AzureStorage:Container" "media"
if (string.IsNullOrWhiteSpace(azureStorageSettings.ConnectionString))
{
    throw new InvalidOperationException("Missing configuration value: AzureStorage:ConnectionString");
}

if (string.IsNullOrWhiteSpace(azureStorageSettings.Container))
{
    throw new InvalidOperationException("Missing configuration value: AzureStorage:Container");
}

builder.Services.AddSingleton(sp =>
{
    var settings = sp.GetRequiredService<IOptions<AzureStorageSettings>>().Value;
    return new BlobServiceClient(settings.ConnectionString);
});

builder.Services.AddDbContext<StitchUpDbContext>(options =>
    options.UseSqlServer(stitchUpSqlConnectionString));

if (builder.Environment.IsDevelopment())
{
    var devUserIdRaw = builder.Configuration["Dev:UserId"];
    if (!Guid.TryParse(devUserIdRaw, out var devUserId))
    {
        throw new InvalidOperationException("Missing or invalid configuration value: Dev:UserId");
    }

    var devUserName = builder.Configuration["Dev:UserName"];
    if (string.IsNullOrWhiteSpace(devUserName))
    {
        devUserName = "dev";
    }

    builder.Services.AddSingleton(new DevUserSettings(devUserId, devUserName));
    builder.Services.AddHostedService<DevUserBootstrapHostedService>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    var storageOptions = app.Services.GetRequiredService<IOptions<AzureStorageSettings>>();
    var blobServiceClient = app.Services.GetRequiredService<BlobServiceClient>();
    ValidateAzureStorageForDevelopment(storageOptions.Value, blobServiceClient, app.Logger);

    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapOpenApi();

    app.MapGet("/api/dev/user", (DevUserSettings devUser) =>
        Results.Ok(new { userId = devUser.UserId, userName = devUser.UserName }));

    app.MapGet("/api/dev/users", async (StitchUpDbContext db, CancellationToken ct) =>
    {
        var desiredNames = new[] { "Jason", "Jackson", "Liam", "dev" };
        var normalized = desiredNames.Select(x => x.ToLower()).ToList();

        var users = await db.Users
            .AsNoTracking()
            .Where(x => normalized.Contains(x.UserName.ToLower()))
            .OrderBy(x => x.UserName)
            .Select(x => new { userId = x.UserId, userName = x.UserName })
            .ToListAsync(ct);

        return Results.Ok(users);
    });
}

if (IsHttpsConfigured(builder.Configuration))
{
    app.UseHttpsRedirection();
}

app.UseMiddleware<UserIdHeaderMiddleware>();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => Results.Text("OK", "text/plain"));
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Manual check examples:
// curl -i -X POST "http://localhost:5000/api/projects" -H "Content-Type: application/json" -d "{\"description\":\"No header\"}"
// curl -i -X POST "http://localhost:5000/api/projects" -H "Content-Type: application/json" -H "X-UserId: <guid>" -d "{\"description\":\"Created from curl\"}"
// curl -i -X POST "http://localhost:5000/api/media/upload-sas" -H "Content-Type: application/json" -H "X-UserId: <guid>" -d "{\"projectId\":\"<project-guid>\",\"mediaId\":\"<media-guid>\",\"fileName\":\"clip.mp4\"}"
app.MapGet("/api/projects", async (HttpContext httpContext, StitchUpDbContext db, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();

    var items = await db.Projects
        .AsNoTracking()
        .Where(x => x.AuthorUserId == currentUserId)
        .OrderByDescending(x => x.UpdatedUtc)
        .Select(x => new ProjectDto
        {
            ProjectId = x.ProjectId,
            AuthorUserId = x.AuthorUserId,
            Title = x.Title,
            Description = x.Description,
            CreatedUtc = x.CreatedUtc,
            UpdatedUtc = x.UpdatedUtc,
        })
        .ToListAsync(ct);

    return Results.Ok(items);
});

app.MapPost("/api/projects", async (CreateProjectDto request, HttpContext httpContext, StitchUpDbContext db, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();

    var userExists = await db.Users
        .AsNoTracking()
        .AnyAsync(x => x.UserId == currentUserId, ct);

    if (!userExists)
    {
        return Results.BadRequest("Current user does not exist.");
    }

    var now = DateTime.UtcNow;
    var normalizedTitle = BuildProjectTitle(request.Title, request.Description);
    var project = new ProjectEntity
    {
        ProjectId = Guid.NewGuid(),
        AuthorUserId = currentUserId,
        Title = normalizedTitle,
        Description = request.Description,
        CreatedUtc = now,
        UpdatedUtc = now
    };

    db.Projects.Add(project);
    await db.SaveChangesAsync(ct);

    var response = new ProjectDto
    {
        ProjectId = project.ProjectId,
        AuthorUserId = project.AuthorUserId,
        Title = project.Title,
        Description = project.Description,
        CreatedUtc = project.CreatedUtc,
        UpdatedUtc = project.UpdatedUtc
    };

    return Results.Ok(response);
});

app.MapPost("/api/media", async (CreateMediaDto request, HttpContext httpContext, StitchUpDbContext db, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();

    var userExists = await db.Users
        .AsNoTracking()
        .AnyAsync(x => x.UserId == currentUserId, ct);

    if (!userExists)
    {
        return Results.BadRequest("Current user does not exist.");
    }

    var media = new MediaEntity
    {
        MediaId = request.MediaId,
        AuthorUserId = currentUserId,
        MediaType = request.MediaType,
        Title = request.Title,
        Description = request.Description,
        BlobPath = request.BlobPath,
        CreatedUtc = DateTime.UtcNow
    };

    db.Media.Add(media);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/api/media/{media.MediaId}", new { mediaId = media.MediaId });
});

app.MapPost("/api/media/upload-sas", async (UploadSasRequestDto request, HttpContext httpContext, BlobServiceClient blobServiceClient, IOptions<AzureStorageSettings> storageOptions, StitchUpDbContext db, ILogger<Program> logger, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();
    var storageSettings = storageOptions.Value;

    var project = await db.Projects
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.ProjectId == request.ProjectId, ct);
    if (project is null)
    {
        return Results.NotFound("Project not found.");
    }

    if (project.AuthorUserId != currentUserId)
    {
        return Results.Forbid();
    }

    if (string.IsNullOrWhiteSpace(request.FileName))
    {
        return Results.BadRequest("fileName is required.");
    }

    var safeFileName = Path.GetFileName(request.FileName.Trim());
    if (string.IsNullOrWhiteSpace(safeFileName))
    {
        return Results.BadRequest("fileName is invalid.");
    }

    var blobPath = $"user/{currentUserId}/projects/{request.ProjectId}/media/{request.MediaId}/{safeFileName}";
    var expiresUtc = DateTime.UtcNow.AddMinutes(15);

    logger.LogInformation(
        "Generating upload SAS. Container: {Container}, AccountName: {AccountNameMasked}",
        storageSettings.Container,
        MaskAccountName(blobServiceClient.AccountName));

    var containerClient = blobServiceClient.GetBlobContainerClient(storageSettings.Container);
    var blobClient = containerClient.GetBlobClient(blobPath);

    if (!blobClient.CanGenerateSasUri)
    {
        return Results.Problem("Storage client cannot generate SAS URI with current configuration.");
    }

    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = storageSettings.Container,
        BlobName = blobPath,
        Resource = "b",
        ExpiresOn = expiresUtc
    };
    sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write | BlobSasPermissions.Read);

    var uploadUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
    var response = new UploadSasResponseDto
    {
        BlobPath = blobPath,
        UploadUrl = uploadUrl,
        ExpiresUtc = expiresUtc
    };

    return Results.Ok(response);
});

app.MapPost("/api/media/read-sas", async (ReadSasRequestDto request, HttpContext httpContext, BlobServiceClient blobServiceClient, IOptions<AzureStorageSettings> storageOptions, StitchUpDbContext db, ILogger<Program> logger, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();
    var storageSettings = storageOptions.Value;

    if (request.ProjectId == Guid.Empty || request.MediaId == Guid.Empty)
    {
        return Results.BadRequest("projectId and mediaId are required.");
    }

    var userExists = await db.Users
        .AsNoTracking()
        .AnyAsync(x => x.UserId == currentUserId, ct);
    if (!userExists)
    {
        return Results.BadRequest("Current user does not exist.");
    }

    var clip = await db.ProjectMedia
        .AsNoTracking()
        .Include(x => x.Media)
        .FirstOrDefaultAsync(
            x => x.ProjectId == request.ProjectId && x.MediaId == request.MediaId,
            ct);

    if (clip?.Media is null)
    {
        return Results.NotFound("Media item was not found in the project.");
    }

    if (string.IsNullOrWhiteSpace(clip.Media.BlobPath))
    {
        return Results.Problem(
            title: "Blob path missing",
            detail: "Media row does not contain a blob path.",
            statusCode: 500);
    }

    try
    {
        var response = BuildReadSasResponse(blobServiceClient, storageSettings, clip.Media.BlobPath, logger);
        return Results.Ok(response);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed generating read SAS for mediaId {MediaId} in projectId {ProjectId}.", request.MediaId, request.ProjectId);
        return Results.Problem(
            title: "Azure Storage configuration invalid",
            detail: "Unable to generate read SAS URL. Verify AzureStorage settings.",
            statusCode: 500);
    }
});

app.MapPost("/api/media/read-sas/batch", async (ReadSasBatchRequestDto request, HttpContext httpContext, BlobServiceClient blobServiceClient, IOptions<AzureStorageSettings> storageOptions, StitchUpDbContext db, ILogger<Program> logger, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();
    var storageSettings = storageOptions.Value;

    if (request.ProjectId == Guid.Empty)
    {
        return Results.BadRequest("projectId is required.");
    }

    var requestedMediaIds = request.MediaIds
        .Where(x => x != Guid.Empty)
        .Distinct()
        .ToList();
    if (requestedMediaIds.Count == 0)
    {
        return Results.BadRequest("mediaIds is required.");
    }

    var userExists = await db.Users
        .AsNoTracking()
        .AnyAsync(x => x.UserId == currentUserId, ct);
    if (!userExists)
    {
        return Results.BadRequest("Current user does not exist.");
    }

    var clips = await db.ProjectMedia
        .AsNoTracking()
        .Include(x => x.Media)
        .Where(x => x.ProjectId == request.ProjectId && requestedMediaIds.Contains(x.MediaId))
        .ToListAsync(ct);

    var responses = new Dictionary<Guid, ReadSasResponseDto>();
    foreach (var clip in clips)
    {
        if (clip.Media is null || string.IsNullOrWhiteSpace(clip.Media.BlobPath))
        {
            continue;
        }

        try
        {
            responses[clip.MediaId] = BuildReadSasResponse(blobServiceClient, storageSettings, clip.Media.BlobPath, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed generating batch read SAS for mediaId {MediaId} in projectId {ProjectId}.", clip.MediaId, request.ProjectId);
        }
    }

    return Results.Ok(responses);
});

app.MapPost("/api/projects/{projectId:guid}/clips", async (Guid projectId, CreateProjectMediaDto request, HttpContext httpContext, StitchUpDbContext db, CancellationToken ct) =>
{
    var currentUserId = httpContext.GetCurrentUserId();

    if (request.ProjectId != projectId)
    {
        return Results.BadRequest("Route projectId must match body ProjectId.");
    }

    var project = await db.Projects
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.ProjectId == projectId, ct);

    if (project is null)
    {
        return Results.NotFound();
    }

    if (project.AuthorUserId != currentUserId)
    {
        return Results.Forbid();
    }

    var clip = new ProjectMediaEntity
    {
        ProjectMediaId = Guid.NewGuid(),
        ProjectId = request.ProjectId,
        MediaId = request.MediaId,
        OrderIndex = request.OrderIndex,
        ItemTitle = request.ItemTitle,
        ItemDescription = request.ItemDescription,
        AddedUtc = DateTime.UtcNow
    };

    db.ProjectMedia.Add(clip);
    await db.SaveChangesAsync(ct);

    return Results.Created($"/api/projects/{projectId}/clips/{clip.ProjectMediaId}", new { projectMediaId = clip.ProjectMediaId });
});

async Task<IResult> MapFeedProjects(HttpContext httpContext, BlobServiceClient blobServiceClient, IOptions<AzureStorageSettings> storageOptions, StitchUpDbContext db, ILogger<Program> logger, CancellationToken ct)
{
    var storageSettings = storageOptions.Value;
    if (string.IsNullOrWhiteSpace(storageSettings.Container))
    {
        return Results.Problem(
            title: "Azure Storage configuration invalid",
            detail: "AzureStorage:Container is missing.",
            statusCode: 500);
    }

    if (string.IsNullOrWhiteSpace(blobServiceClient.AccountName))
    {
        return Results.Problem(
            title: "Azure Storage configuration invalid",
            detail: "Azure Storage account is missing or malformed.",
            statusCode: 500);
    }

    var currentUserId = TryReadHeaderUserId(httpContext);

    var projectQuery = db.Projects
        .AsNoTracking()
        .Include(x => x.AuthorUser)
        .Include(x => x.ProjectMedia)
            .ThenInclude(x => x.Media)
        .AsQueryable();

    if (currentUserId.HasValue)
    {
        projectQuery = projectQuery.Where(x => x.AuthorUserId != currentUserId.Value);
    }

    var projects = await projectQuery
        .OrderByDescending(x => x.UpdatedUtc)
        .ToListAsync(ct);

    try
    {
        var result = projects.Select(project => new FeedProjectDto
        {
            ProjectId = project.ProjectId,
            AuthorUserId = project.AuthorUserId,
            AuthorUserName = project.AuthorUser?.UserName ?? string.Empty,
            Title = project.Title,
            Description = project.Description,
            UpdatedUtc = project.UpdatedUtc,
            Clips = project.ProjectMedia
                .OrderBy(clip => clip.OrderIndex)
                .Select(clip =>
                {
                    var blobPath = (clip.Media?.BlobPath ?? string.Empty).TrimStart('/');
                    var blobClient = blobServiceClient
                        .GetBlobContainerClient(storageSettings.Container)
                        .GetBlobClient(blobPath);

                    if (!blobClient.CanGenerateSasUri)
                    {
                        throw new InvalidOperationException(
                            "Blob client cannot generate SAS URI with current AzureStorage configuration.");
                    }

                    var readSasBuilder = new BlobSasBuilder
                    {
                        BlobContainerName = storageSettings.Container,
                        BlobName = blobPath,
                        Resource = "b",
                        ExpiresOn = DateTimeOffset.UtcNow.AddHours(1)
                    };
                    readSasBuilder.SetPermissions(BlobSasPermissions.Read);

                    return new FeedClipDto
                    {
                        MediaId = clip.MediaId,
                        OrderIndex = clip.OrderIndex,
                        BlobPath = blobPath,
                        ReadUrl = blobClient.GenerateSasUri(readSasBuilder).ToString(),
                        WasCloudConverted = clip.Media?.WasCloudConverted ?? false,
                        CloudConversionStatus = clip.Media?.CloudConversionStatus ?? string.Empty
                    };
                })
                .ToList()
        }).ToList();

        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed generating read SAS for feed projects.");
        return Results.Problem(
            title: "Azure Storage configuration invalid",
            detail: "Unable to generate read SAS URLs for feed clips. Verify AzureStorage settings.",
            statusCode: 500);
    }
}

app.MapGet("/api/feed/projects", MapFeedProjects);
app.MapGet("/api/projects/feed", MapFeedProjects);

app.MapGet("/api/projects/{projectId:guid}/clips", async (Guid projectId, StitchUpDbContext db, CancellationToken ct) =>
{
    var projectExists = await db.Projects
        .AsNoTracking()
        .AnyAsync(x => x.ProjectId == projectId, ct);

    if (!projectExists)
    {
        return Results.NotFound();
    }

    var clips = await db.ProjectMedia
        .AsNoTracking()
        .Where(pm => pm.ProjectId == projectId)
        .OrderBy(pm => pm.OrderIndex)
        .Select(pm => new ProjectClipDto
        {
            ProjectMediaId = pm.ProjectMediaId,
            ProjectId = pm.ProjectId,
            OrderIndex = pm.OrderIndex,
            ItemTitle = pm.ItemTitle,
            ItemDescription = pm.ItemDescription,
            AddedUtc = pm.AddedUtc,
            MediaId = pm.Media.MediaId,
            MediaType = pm.Media.MediaType,
            Title = pm.Media.Title,
            Description = pm.Media.Description,
            BlobPath = pm.Media.BlobPath,
            CreatedUtc = pm.Media.CreatedUtc
        })
        .ToListAsync(ct);

    return Results.Ok(clips);
});

app.Run();

static void ValidateAzureStorageForDevelopment(AzureStorageSettings settings, BlobServiceClient blobServiceClient, ILogger logger)
{
    logger.LogInformation(
        "Azure Storage configuration validated for development. AccountName: {AccountNameMasked}, Container: {Container}",
        MaskAccountName(blobServiceClient.AccountName),
        settings.Container);
}

static bool IsHttpsConfigured(IConfiguration configuration)
{
    var configuredUrls = configuration["urls"] ?? configuration["ASPNETCORE_URLS"];
    if (!string.IsNullOrWhiteSpace(configuredUrls))
    {
        var hasHttpsUrl = configuredUrls
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(url => url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        if (hasHttpsUrl)
        {
            return true;
        }
    }

    return configuration
        .GetSection("Kestrel:Endpoints")
        .GetChildren()
        .Select(x => x["Url"])
        .Any(url => !string.IsNullOrWhiteSpace(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}

static string MaskAccountName(string accountName)
{
    if (string.IsNullOrWhiteSpace(accountName) || accountName == "<unknown>")
    {
        return "<unknown>";
    }

    if (accountName.Length <= 4)
    {
        return "****";
    }

    return $"{accountName[..2]}***{accountName[^2..]}";
}

static string BuildProjectTitle(string? title, string? description)
{
    var explicitTitle = title?.Trim();
    if (!string.IsNullOrWhiteSpace(explicitTitle))
    {
        return Truncate(explicitTitle, 200);
    }

    var normalizedDescription = description?.Trim();
    if (!string.IsNullOrWhiteSpace(normalizedDescription))
    {
        var summary = Truncate(normalizedDescription, 40);
        return Truncate(summary, 200);
    }

    return "Post";
}

static string Truncate(string value, int maxLength)
{
    if (value.Length <= maxLength)
    {
        return value;
    }

    return value[..maxLength];
}

static Guid? TryReadHeaderUserId(HttpContext httpContext)
{
    var rawUserId = httpContext.Request.Headers["X-UserId"].FirstOrDefault();
    if (Guid.TryParse(rawUserId, out var parsedUserId) && parsedUserId != Guid.Empty)
    {
        return parsedUserId;
    }

    return null;
}

static ReadSasResponseDto BuildReadSasResponse(
    BlobServiceClient blobServiceClient,
    AzureStorageSettings storageSettings,
    string blobPath,
    ILogger logger)
{
    if (string.IsNullOrWhiteSpace(storageSettings.Container))
    {
        throw new InvalidOperationException("AzureStorage:Container is missing.");
    }

    var normalizedBlobPath = blobPath.TrimStart('/');
    var expiresUtc = DateTime.UtcNow.AddHours(1);
    var containerClient = blobServiceClient.GetBlobContainerClient(storageSettings.Container);
    var blobClient = containerClient.GetBlobClient(normalizedBlobPath);

    if (!blobClient.CanGenerateSasUri)
    {
        throw new InvalidOperationException("Blob client cannot generate SAS URI with current configuration.");
    }

    logger.LogInformation(
        "Generating read SAS. Container: {Container}, AccountName: {AccountNameMasked}",
        storageSettings.Container,
        MaskAccountName(blobServiceClient.AccountName));

    var sasBuilder = new BlobSasBuilder
    {
        BlobContainerName = storageSettings.Container,
        BlobName = normalizedBlobPath,
        Resource = "b",
        ExpiresOn = expiresUtc
    };
    sasBuilder.SetPermissions(BlobSasPermissions.Read);

    return new ReadSasResponseDto
    {
        Url = blobClient.GenerateSasUri(sasBuilder).ToString(),
        ExpiresUtc = expiresUtc
    };
}

internal sealed class AzureStorageSettings
{
    public string ConnectionString { get; init; } = string.Empty;
    public string Container { get; init; } = string.Empty;
}

internal sealed record DevUserSettings(Guid UserId, string UserName);

internal sealed class XUserIdHeaderOperationFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public XUserIdHeaderOperationFilter(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public void Apply(Microsoft.OpenApi.OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {
        var relativePath = context.ApiDescription.RelativePath ?? string.Empty;
        var normalizedPath = "/" + relativePath.TrimStart('/').Split('?', '#')[0].ToLowerInvariant();
        if (!ShouldApplyHeaderParameters(normalizedPath))
        {
            return;
        }

        var parameters = operation.Parameters?.ToList() ?? new List<Microsoft.OpenApi.IOpenApiParameter>();

        var isOptionalHeaderPath = normalizedPath is "/api/feed/projects" or "/api/projects/feed";

        if (!parameters.Any(p => string.Equals(p.Name, "X-UserId", StringComparison.OrdinalIgnoreCase)))
        {
            var userIdParameter = new Microsoft.OpenApi.OpenApiParameter
            {
                Name = "X-UserId",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Required = !isOptionalHeaderPath,
                Description = isOptionalHeaderPath
                    ? "Optional current user id. If provided, feed excludes projects owned by this user."
                    : "Effective user id for this request. Server derives author/ownership from this header.",
                Schema = new Microsoft.OpenApi.OpenApiSchema
                {
                    Type = Microsoft.OpenApi.JsonSchemaType.String,
                    Format = "uuid"
                }
            };

            if (_environment.IsDevelopment())
            {
                var devUserId = _configuration["Dev:UserId"];
                if (!string.IsNullOrWhiteSpace(devUserId))
                {
                    userIdParameter.Description += $" Default (Dev): {devUserId}";
                    userIdParameter.Examples = new Dictionary<string, Microsoft.OpenApi.IOpenApiExample>
                    {
                        ["dev"] = new Microsoft.OpenApi.OpenApiExample
                        {
                            Summary = "Development default",
                            Value = System.Text.Json.Nodes.JsonValue.Create(devUserId)
                        }
                    };
                }
            }

            parameters.Add(userIdParameter);
        }

        if (!parameters.Any(p => string.Equals(p.Name, "X-UserName", StringComparison.OrdinalIgnoreCase)))
        {
            var userNameParameter = new Microsoft.OpenApi.OpenApiParameter
            {
                Name = "X-UserName",
                In = Microsoft.OpenApi.ParameterLocation.Header,
                Required = false,
                Description = "Optional display/user context header for future use.",
                Schema = new Microsoft.OpenApi.OpenApiSchema
                {
                    Type = Microsoft.OpenApi.JsonSchemaType.String
                }
            };

            if (_environment.IsDevelopment())
            {
                var devUserName = _configuration["Dev:UserName"];
                if (!string.IsNullOrWhiteSpace(devUserName))
                {
                    userNameParameter.Description += $" Default (Dev): {devUserName}";
                    userNameParameter.Examples = new Dictionary<string, Microsoft.OpenApi.IOpenApiExample>
                    {
                        ["dev"] = new Microsoft.OpenApi.OpenApiExample
                        {
                            Summary = "Development default",
                            Value = System.Text.Json.Nodes.JsonValue.Create(devUserName)
                        }
                    };
                }
            }

            parameters.Add(userNameParameter);
        }

        operation.Parameters = parameters;
    }

    private static bool ShouldApplyHeaderParameters(string normalizedPath)
    {
        if (!normalizedPath.StartsWith("/api/", StringComparison.Ordinal))
        {
            return false;
        }

        return normalizedPath is not "/api/health" and not "/api/dev/user";
    }
}

public partial class Program
{
}
