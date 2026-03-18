using System.Collections.Concurrent;

namespace TokenForeman.Middleware;

/// <summary>
/// In-memory rate limiter by client IP. Applied to API routes to reduce abuse.
/// Permission boundary: this middleware only inspects IP (from connection/headers); it does not access tokens or body.
/// </summary>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly int _perWindow;
    private readonly TimeSpan _window;
    private static readonly ConcurrentDictionary<string, WindowCount> Store = new();

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger, int perWindow = 60, TimeSpan? window = null)
    {
        _next = next;
        _logger = logger;
        _perWindow = perWindow;
        _window = window ?? TimeSpan.FromMinutes(1);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (string.IsNullOrEmpty(key)) key = "unknown";

        var now = DateTime.UtcNow;
        var allowed = TryIncrement(key, now);
        if (!allowed)
        {
            _logger.LogWarning("Rate limit exceeded for client {ClientKey}, path {Path}", MaskIp(key), path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = _window.TotalSeconds.ToString("F0");
            await context.Response.WriteAsJsonAsync(new { error = "rate_limit_exceeded", message = "Too many requests. Try again later." });
            return;
        }

        await _next(context);
    }

    private bool TryIncrement(string key, DateTime now)
    {
        var entry = Store.AddOrUpdate(
            key,
            _ => new WindowCount(now, 1),
            (_, w) =>
            {
                if (now - w.Start > _window)
                    return new WindowCount(now, 1);
                return new WindowCount(w.Start, w.Count + 1);
            });

        if (now - entry.Start > _window)
        {
            Store.TryUpdate(key, new WindowCount(now, 1), entry);
            return true;
        }

        // Allow only when count is within limit; after increment, 61st request has count 61 so we deny.
        return entry.Count <= _perWindow;
    }

    private static string MaskIp(string ip)
    {
        if (string.IsNullOrEmpty(ip) || ip == "unknown") return "unknown";
        var lastDot = ip.LastIndexOf('.');
        if (lastDot > 0) return $"{ip[..lastDot]}.***";
        return "***";
    }

    private sealed record WindowCount(DateTime Start, int Count);
}
