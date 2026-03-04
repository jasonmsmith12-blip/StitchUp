using Microsoft.EntityFrameworkCore;
using StitchUp.Domain.Entities.Server;
using StitchUp.Infrastructure.Data;

namespace StitchUp.Api.Startup;

internal sealed class DevUserBootstrapHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DevUserSettings _devUser;
    private readonly ILogger<DevUserBootstrapHostedService> _logger;

    private static readonly string[] RequiredUserNames =
    {
        "Jason",
        "Jackson",
        "Liam"
    };

    public DevUserBootstrapHostedService(
        IServiceProvider serviceProvider,
        DevUserSettings devUser,
        ILogger<DevUserBootstrapHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _devUser = devUser;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StitchUpDbContext>();

        var desiredUsers = new List<(string UserName, Guid? RequiredUserId)>
        {
            (_devUser.UserName, _devUser.UserId)
        };
        desiredUsers.AddRange(RequiredUserNames.Select(x => (x, (Guid?)null)));

        var normalizedNames = desiredUsers
            .Select(x => x.UserName.Trim().ToLower())
            .Distinct()
            .ToList();

        var existingUsers = await db.Users
            .Where(x => normalizedNames.Contains(x.UserName.ToLower()))
            .ToListAsync(cancellationToken);

        var existingByName = existingUsers
            .ToDictionary(x => x.UserName.ToLower(), x => x);

        var createdUsers = new List<string>();
        var reusedUsers = new List<string>();

        foreach (var desired in desiredUsers)
        {
            var normalizedName = desired.UserName.Trim().ToLower();
            if (existingByName.ContainsKey(normalizedName))
            {
                reusedUsers.Add(desired.UserName);
                continue;
            }

            var user = new UserEntity
            {
                UserId = desired.RequiredUserId ?? Guid.NewGuid(),
                UserName = desired.UserName,
                Email = $"{normalizedName}@stitchup.local",
                CreatedUtc = DateTime.UtcNow
            };

            db.Users.Add(user);
            existingByName[normalizedName] = user;
            createdUsers.Add(desired.UserName);
        }

        if (createdUsers.Count == 0)
        {
            _logger.LogInformation(
                "Dev user bootstrap complete. Reused users: {Users}",
                string.Join(", ", reusedUsers.Distinct(StringComparer.OrdinalIgnoreCase)));
            return;
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Dev user bootstrap complete. Created users: {Created}. Reused users: {Reused}",
            string.Join(", ", createdUsers.Distinct(StringComparer.OrdinalIgnoreCase)),
            string.Join(", ", reusedUsers.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
