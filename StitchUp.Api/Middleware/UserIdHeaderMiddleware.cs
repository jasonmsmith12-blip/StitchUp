using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace StitchUp.Api.Middleware;

public sealed class UserIdHeaderMiddleware
{
    private const string UserIdHeader = "X-UserId";
    public const string CurrentUserIdItemKey = "CurrentUserId";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly IHostEnvironment _environment;

    public UserIdHeaderMiddleware(
        RequestDelegate next,
        IHostEnvironment environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (IsSwaggerPath(path) || !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (IsExemptApiPath(path))
        {
            await _next(context);
            return;
        }

        var rawUserId = context.Request.Headers[UserIdHeader].FirstOrDefault();
        if (!Guid.TryParse(rawUserId, out var parsedUserId) || parsedUserId == Guid.Empty)
        {
            await WriteUnauthorizedAsync(context, _environment.IsDevelopment());
            return;
        }

        context.Items[CurrentUserIdItemKey] = parsedUserId;

        await _next(context);
    }

    private static bool IsExemptApiPath(PathString path)
    {
        return path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/api/dev/user", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/api/dev/users", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/api/feed/projects", StringComparison.OrdinalIgnoreCase) ||
               path.Equals("/api/projects/feed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSwaggerPath(PathString path)
    {
        return path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase);
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, bool includeHelpfulDetail)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.ContentType = "application/json";
        var message = includeHelpfulDetail
            ? "Missing or invalid X-UserId header. Include a valid GUID in request header 'X-UserId'."
            : "Missing or invalid X-UserId header.";
        var payload = JsonSerializer.Serialize(
            new { error = message },
            JsonOptions);
        return context.Response.WriteAsync(payload);
    }
}
