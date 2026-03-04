using Microsoft.AspNetCore.Http;
using StitchUp.Api.Middleware;

namespace StitchUp.Api.Startup;

public interface ICurrentUserAccessor
{
    Guid CurrentUserId { get; }
}

internal sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserAccessor(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid CurrentUserId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context?.Items.TryGetValue(UserIdHeaderMiddleware.CurrentUserIdItemKey, out var value) == true &&
                value is Guid parsedUserId &&
                parsedUserId != Guid.Empty)
            {
                return parsedUserId;
            }

            throw new UnauthorizedAccessException("Missing or invalid X-UserId header.");
        }
    }
}
