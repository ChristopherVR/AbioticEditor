using System.CommandLine;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>download-wiki-images</c> - a maintainer command that fetches the verified
/// <see cref="WikiImageManifest"/> set from abioticfactor.wiki.gg into <c>assets/wiki/</c>.
/// That folder is bundled next to the executable and used by <see cref="WikiImageCache"/> as the
/// offline fallback when the live wiki is unreachable. Re-run it when the catalogs gain entries or
/// the wiki art changes, then commit the refreshed files.
///
/// Files are named with <see cref="WikiImageCache.SafeNameFor"/> plus the actual image extension,
/// exactly matching what the cache writes, so the fallback lookup finds them.
///
/// Wiki content is CC BY-NC-SA: the bundled images carry the <see cref="WikiImageCache.AttributionText"/>
/// credit in the UI; see <c>assets/wiki/README.md</c>.
/// </summary>
internal static class DownloadWikiImagesCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var outOpt = new Option<string?>("--output", "-o")
        {
            Description = "Output folder for the downloaded images (default: ./wiki). Point at the repo's assets/wiki to refresh the bundle.",
        };

        var cmd = new Command("download-wiki-images",
            "Download the verified wiki images into a folder bundled as the offline fallback (maintainer tool; needs network).");
        cmd.Options.Add(outOpt);
        cmd.SetAction(parseResult => Cli.Run(() =>
            Download(parseResult.GetValue(outOpt), parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Download(string? output, bool quiet)
    {
        var outDir = Path.GetFullPath(string.IsNullOrWhiteSpace(output) ? "wiki" : output);
        Directory.CreateDirectory(outDir);

        var files = WikiImageManifest.AllFiles;
        Cli.Info(quiet, $"Downloading {files.Count} wiki image(s) -> {outDir}");

        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AbioticEditor/1.0 (save editor; wiki image bundle)");

        int ok = 0, failed = 0;
        foreach (var file in files)
        {
            try
            {
                var saved = FetchWithRetry(http, file, outDir).GetAwaiter().GetResult();
                if (saved is not null)
                {
                    ok++;
                    Cli.Info(quiet, $"  ok   {file} -> {Path.GetFileName(saved)}");
                }
                else
                {
                    failed++;
                    Cli.Warn($"{file}: no usable image returned (missing on the wiki or not an image).");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                failed++;
                Cli.Warn($"{file}: download failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        Cli.Info(quiet, $"Done: {ok} downloaded, {failed} failed.");
        if (ok == 0)
        {
            throw new CliUserErrorException(
                "no images downloaded - check the network connection and that abioticfactor.wiki.gg is reachable.");
        }
        Cli.Info(quiet, "Commit the folder under assets/wiki/ to bundle the offline fallback with the editor.");
        return Cli.Ok;
    }

    /// <summary>
    /// Fetches one image, retrying transient failures. wiki.gg rate-limits rapid sequential
    /// requests (a burst returns HTML challenge / 429 pages), so a short backoff between
    /// attempts is what turns a "missing" tail back into successful downloads. <c>null</c> is
    /// returned only after all attempts come back without a usable image.
    /// </summary>
    private static async Task<string?> FetchWithRetry(HttpClient http, string file, string outDir)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            // Be polite to the wiki between requests; back off harder after a miss.
            await Task.Delay(TimeSpan.FromMilliseconds(attempt == 1 ? 400 : 1500 * (attempt - 1))).ConfigureAwait(false);

            string? saved = null;
            try
            {
                saved = await FetchOne(http, file, outDir).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                if (attempt >= maxAttempts) throw;
                continue;
            }

            if (saved is not null || attempt >= maxAttempts) return saved;
        }
    }

    private static async Task<string?> FetchOne(HttpClient http, string file, string outDir)
    {
        using var response = await http.GetAsync(WikiImageCache.FilePathUrlFor(file)).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var data = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var extension = WikiImageCache.ExtensionForImage(data);
        if (extension is null) return null; // HTML error page or unsupported format.

        var finalPath = Path.Combine(outDir, WikiImageCache.SafeNameFor(file) + extension);
        var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllBytesAsync(tempPath, data).ConfigureAwait(false);
        File.Move(tempPath, finalPath, overwrite: true);
        return finalPath;
    }
}
