using System.Net;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Defines the only endpoint that the packaged local editor is permitted to bind.
/// A save editor must never accidentally become reachable from a LAN because it can
/// read and write local files selected by its user.
/// </summary>
public static class LocalHostEndpoint
{
    public const string DefaultUrl = "http://127.0.0.1:37246";

    public static string Resolve(string? configuredUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredUrl) ? DefaultUrl : configuredUrl.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint)
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || endpoint.Port is < 1024 or > 65535
            || !IsLoopback(endpoint.Host)
            || endpoint.AbsolutePath != "/"
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "ABIOTIC_EDITOR_URL must be an http loopback URL such as http://127.0.0.1:37246.");
        }

        return endpoint.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsLoopback(string host)
        => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
           || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
