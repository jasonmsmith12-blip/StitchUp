using Microsoft.AspNetCore.Http;
using StitchUp.Api.Middleware;

namespace StitchUp.Api.Extensions;

public static class HttpContextExtensions
{
    public static Guid GetCurrentUserId(this HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(UserIdHeaderMiddleware.CurrentUserIdItemKey, out var value) &&
            value is Guid currentUserId &&
            currentUserId != Guid.Empty)
        {
            return currentUserId;
        }

        throw new UnauthorizedAccessException("Missing or invalid X-UserId header.");
    }
}
