using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StitchUp.Infrastructure.Data;

public class StitchUpDbContextFactory : IDesignTimeDbContextFactory<StitchUpDbContext>
{
    private const string PlaceholderConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=StitchUp;Trusted_Connection=True;TrustServerCertificate=True";

    public StitchUpDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("STITCHUP_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = PlaceholderConnectionString;
        }

        var optionsBuilder = new DbContextOptionsBuilder<StitchUpDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new StitchUpDbContext(optionsBuilder.Options);
    }
}
