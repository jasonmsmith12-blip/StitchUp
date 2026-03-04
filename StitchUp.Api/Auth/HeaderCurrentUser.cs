using Microsoft.AspNetCore.Http;
using StitchUp.Api.Middleware;

namespace StitchUp.Api.Auth;

public interface ICurrentUser
{
    Guid UserId { get; }

    string? UserName { get; }
}

public sealed class HeaderCurrentUser : ICurrentUser
{
    private const string UserIdHeader = "X-UserId";
    private const string UserNameHeader = "X-UserName";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HeaderCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context?.Items.TryGetValue(UserIdHeaderMiddleware.CurrentUserIdItemKey, out var value) == true &&
                value is Guid currentUserId &&
                currentUserId != Guid.Empty)
            {
                return currentUserId;
            }

            var rawValue = context?.Request.Headers[UserIdHeader].FirstOrDefault();
            return Guid.TryParse(rawValue, out var parsed) ? parsed : Guid.Empty;
        }
    }

    public string? UserName
    {
        get
        {
            var rawValue = _httpContextAccessor.HttpContext?.Request.Headers[UserNameHeader].FirstOrDefault();
            return string.IsNullOrWhiteSpace(rawValue) ? null : rawValue;
        }
    }
}
