using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StitchUp.Domain.Entities.Server;
using StitchUp.Infrastructure.Data;

namespace StitchUp.Api.Tests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public static readonly Guid SeedUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly string _databaseName = $"stitchup_integration_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");

        var sqlConnectionString = $"Server=(localdb)\\mssqllocaldb;Database={_databaseName};Trusted_Connection=True;TrustServerCertificate=True;";
        Environment.SetEnvironmentVariable("ConnectionStrings__StitchUpSql", sqlConnectionString);
        Environment.SetEnvironmentVariable("AzureStorage__Container", "media");
        Environment.SetEnvironmentVariable("AzureStorage__ConnectionString", "UseDevelopmentStorage=true");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:StitchUpSql"] = sqlConnectionString,
                ["AzureStorage:Container"] = "media",
                ["AzureStorage:ConnectionString"] = "UseDevelopmentStorage=true"
            });
        });

        builder.ConfigureServices(services =>
        {
            using var scope = services.BuildServiceProvider().CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StitchUpDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            if (!db.Users.Any(x => x.UserId == SeedUserId))
            {
                db.Users.Add(new UserEntity
                {
                    UserId = SeedUserId,
                    UserName = "integration-user",
                    Email = "integration@stitchup.local",
                    CreatedUtc = DateTime.UtcNow
                });
                db.SaveChanges();
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__StitchUpSql", null);
            Environment.SetEnvironmentVariable("AzureStorage__Container", null);
            Environment.SetEnvironmentVariable("AzureStorage__ConnectionString", null);
            try
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<StitchUpDbContext>();
                db.Database.EnsureDeleted();
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
