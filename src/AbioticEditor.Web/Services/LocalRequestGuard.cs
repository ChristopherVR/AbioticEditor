namespace AbioticEditor.Web.Services;

/// <summary>
/// Rejects requests that did not come from the editor's own window. The server only listens on
/// loopback, but a web page open in the player's browser can still reach loopback, either with a
/// forged cross-site request or by pointing its own domain name at 127.0.0.1 (DNS rebinding).
/// Both show up as a Host or Origin that is not the editor's own address.
/// </summary>
public static class LocalRequestGuard
{
    public static bool IsAllowed(string? host, string? origin, int port)
    {
        if (!IsLocalAuthority(host, port)) return false;
        if (string.IsNullOrEmpty(origin)) return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && IsLocalAuthority(uri.Authority, port);
    }

    private static bool IsLocalAuthority(string? authority, int port)
    {
        if (string.IsNullOrEmpty(authority)
            || !Uri.TryCreate("http://" + authority, UriKind.Absolute, out var uri)
            || uri.Port != port)
        {
            return false;
        }
        return uri.Host is "localhost" or "127.0.0.1" or "[::1]";
    }

    public static IApplicationBuilder UseLocalRequestGuard(this IApplicationBuilder app, int port)
        => app.Use(async (context, next) =>
        {
            if (!IsAllowed(context.Request.Headers.Host.ToString(), context.Request.Headers.Origin.ToString(), port))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });
}
