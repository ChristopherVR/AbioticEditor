using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AbioticEditor.Updater;

/// <summary>
/// Reads release metadata from the GitHub REST API. Only the two read-only endpoints the
/// updater needs are implemented: <c>releases/latest</c> (newest non-prerelease) and the
/// <c>releases</c> list (used when pre-releases are allowed). No token is required for a
/// public repository.
/// </summary>
public sealed class GitHubReleaseClient
{
    private readonly UpdaterOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public GitHubReleaseClient(UpdaterOptions options, HttpClient? http = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsHttp = http is null;
        _http = http ?? new HttpClient();
        ConfigureDefaults(_http, options);
    }

    private static void ConfigureDefaults(HttpClient http, UpdaterOptions options)
    {
        // GitHub rejects requests with no User-Agent.
        if (http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue(Sanitize(options.ProductName), "1.0"));
        }
        if (http.DefaultRequestHeaders.Accept.Count == 0)
        {
            http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
        if (!string.IsNullOrWhiteSpace(options.GitHubToken)
            && http.DefaultRequestHeaders.Authorization is null)
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", options.GitHubToken);
        }
    }

    /// <summary>
    /// Returns the release the updater should consider the "latest" for this build:
    /// when <see cref="UpdaterOptions.AllowPrerelease"/> is false this is the GitHub
    /// "latest" release; otherwise it is the newest published release of any kind.
    /// Returns null when the repo has no eligible release.
    /// </summary>
    public async Task<GitHubRelease?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        if (_options.IsPlaceholderRepository)
        {
            throw new UpdaterConfigurationException(
                "The update repository has not been configured yet. Set "
                + $"{nameof(UpdaterOptions)}.{nameof(UpdaterOptions.RepositoryOwner)} / "
                + $"{nameof(UpdaterOptions.RepositoryName)} to your published GitHub repo.");
        }

        if (_options.AllowPrerelease)
        {
            var all = await GetReleasesAsync(cancellationToken).ConfigureAwait(false);
            return all
                .Where(r => !r.Draft)
                .OrderByDescending(r => r.Version, ReleaseVersionComparer.Instance)
                .FirstOrDefault();
        }

        using var response = await _http
            .GetAsync($"{_options.ApiBaseUrl}/releases/latest", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 here means "no published full release yet", not a hard error.
            return null;
        }
        await ThrowIfApiError(response).ConfigureAwait(false);

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseRelease(doc.RootElement);
    }

    /// <summary>Lists published releases (newest first as GitHub returns them).</summary>
    public async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http
            .GetAsync($"{_options.ApiBaseUrl}/releases?per_page=30", cancellationToken)
            .ConfigureAwait(false);
        await ThrowIfApiError(response).ConfigureAwait(false);

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var list = new List<GitHubRelease>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                list.Add(ParseRelease(element));
            }
        }
        return list;
    }

    private static GitHubRelease ParseRelease(JsonElement e)
    {
        var assets = new List<ReleaseAsset>();
        if (e.TryGetProperty("assets", out var assetArray)
            && assetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assetArray.EnumerateArray())
            {
                var name = GetString(a, "name");
                var url = GetString(a, "browser_download_url");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                {
                    continue;
                }
                assets.Add(new ReleaseAsset
                {
                    Name = name,
                    DownloadUrl = url,
                    Size = a.TryGetProperty("size", out var size) && size.TryGetInt64(out var s) ? s : 0,
                });
            }
        }

        return new GitHubRelease
        {
            TagName = GetString(e, "tag_name"),
            Name = GetString(e, "name"),
            Body = GetString(e, "body"),
            Prerelease = e.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True,
            Draft = e.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
            HtmlUrl = GetString(e, "html_url"),
            PublishedAt = e.TryGetProperty("published_at", out var pub)
                && pub.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(pub.GetString(), out var when)
                    ? when
                    : null,
            Assets = assets,
        };
    }

    private static string GetString(JsonElement e, string property)
        => e.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static async Task ThrowIfApiError(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = string.Empty;
        try
        {
            detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort - the status code is the useful part.
        }

        var hint = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => " (rate limited? set a GitHub token)",
            HttpStatusCode.Unauthorized => " (bad or missing GitHub token)",
            _ => string.Empty,
        };
        throw new UpdaterException(
            $"GitHub API request failed: {(int)response.StatusCode} {response.ReasonPhrase}{hint}. {Trim(detail)}");
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;

    private static string Sanitize(string product)
    {
        var cleaned = new string(product.Where(c => char.IsLetterOrDigit(c) || c is '-' or '.' or '_').ToArray());
        return string.IsNullOrEmpty(cleaned) ? "AbioticEditor" : cleaned;
    }

    /// <summary>Orders by parsed version, treating an unparseable tag as the lowest.</summary>
    private sealed class ReleaseVersionComparer : IComparer<ReleaseVersion?>
    {
        public static readonly ReleaseVersionComparer Instance = new();

        public int Compare(ReleaseVersion? x, ReleaseVersion? y)
        {
            if (x is null && y is null)
            {
                return 0;
            }
            if (x is null)
            {
                return -1;
            }
            if (y is null)
            {
                return 1;
            }
            return x.CompareTo(y);
        }
    }
}
