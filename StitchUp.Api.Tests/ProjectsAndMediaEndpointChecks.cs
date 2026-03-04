using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StitchUp.Infrastructure.Data;
using Xunit;

namespace StitchUp.Api.Tests;

public sealed class ProjectsAndMediaEndpointChecks : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CustomWebApplicationFactory _factory;

    public ProjectsAndMediaEndpointChecks(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostProjects_WithoutXUserId_Returns400()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            title = "Header Required",
            description = "Should fail without X-UserId."
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostProjects_WithXUserId_CreatesRowWithMatchingAuthorUserId()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-UserId", CustomWebApplicationFactory.SeedUserId.ToString());

        var response = await client.PostAsJsonAsync("/api/projects", new
        {
            title = "Integration Project",
            description = "Created from endpoint check."
        });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<ProjectCreateResponse>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal(CustomWebApplicationFactory.SeedUserId, created!.AuthorUserId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StitchUpDbContext>();
        var persisted = await db.Projects.FindAsync(created.ProjectId);
        Assert.NotNull(persisted);
        Assert.Equal(CustomWebApplicationFactory.SeedUserId, persisted!.AuthorUserId);
    }

    [Fact]
    public async Task PostUploadSas_WithValidHeader_Returns200()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-UserId", CustomWebApplicationFactory.SeedUserId.ToString());

        var createProjectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            description = "Project for upload-sas test."
        });
        createProjectResponse.EnsureSuccessStatusCode();
        var createdProject = await createProjectResponse.Content.ReadFromJsonAsync<ProjectCreateResponse>(JsonOptions);
        Assert.NotNull(createdProject);

        var mediaId = Guid.NewGuid();
        var response = await client.PostAsJsonAsync("/api/media/upload-sas", new
        {
            projectId = createdProject!.ProjectId,
            mediaId,
            fileName = "clip.mp4"
        });

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<UploadSasResponse>(JsonOptions);
        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload!.BlobPath));
        Assert.False(string.IsNullOrWhiteSpace(payload.UploadUrl));
    }

    private sealed class ProjectCreateResponse
    {
        public Guid ProjectId { get; set; }
        public Guid AuthorUserId { get; set; }
    }

    private sealed class UploadSasResponse
    {
        public string BlobPath { get; set; } = string.Empty;
        public string UploadUrl { get; set; } = string.Empty;
        public DateTime ExpiresUtc { get; set; }
    }
}
