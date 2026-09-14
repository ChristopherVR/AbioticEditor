using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace AbioticEditor.Core.LiveEditing;

/// <summary>Installs a missing UE4SS runtime without replacing an existing mod installation.</summary>
public sealed class LiveRuntimeInstaller(HttpClient http)
{
    public const string ReleaseEndpoint = "https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases/tags/experimental-latest";
    private const long MaxDownloadBytes = 64 * 1024 * 1024;
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public static bool IsInstalled(string win64)
        => File.Exists(Path.Combine(win64, "ue4ss", "UE4SS.dll"))
           && File.Exists(Path.Combine(win64, "ue4ss", "Mods", "shared", "UEHelpers", "UEHelpers.lua"));

    public async Task InstallAsync(string win64, CancellationToken cancellationToken = default)
    {
        await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsInstalled(win64)) return;
            EnsureEmptyTarget(win64);
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseEndpoint);
            request.Headers.UserAgent.ParseAdd("AbioticEditor-LiveSetup/1.0");
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var release = await response.Content.ReadFromJsonAsync<Release>(cancellationToken).ConfigureAwait(false);
            var asset = release?.Assets?.FirstOrDefault(a => a.Name?.StartsWith("UE4SS_", StringComparison.Ordinal) == true
                && a.Name.EndsWith(".zip", StringComparison.Ordinal));
            if (asset is null || asset.Size is <= 0 or > MaxDownloadBytes
                || !Uri.TryCreate(asset.Url, UriKind.Absolute, out var url)
                || url.Scheme != "https" || url.Host != "github.com"
                || !url.AbsolutePath.StartsWith("/UE4SS-RE/RE-UE4SS/releases/download/", StringComparison.Ordinal)
                || asset.Digest is not { Length: 71 } || !asset.Digest.StartsWith("sha256:", StringComparison.Ordinal))
                throw new InvalidDataException("The official live-support download is unavailable. Try again later.");

            using var download = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            download.EnsureSuccessStatusCode();
            await using var source = await download.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var package = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (package.Length + read > MaxDownloadBytes) throw new InvalidDataException("Live-support download is too large.");
                package.Write(buffer, 0, read);
            }
            var digest = Convert.ToHexString(SHA256.HashData(package.GetBuffer().AsSpan(0, (int)package.Length)));
            if (package.Length != asset.Size || !digest.Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Live-support download could not be verified. Please retry setup.");
            package.Position = 0;
            await InstallPackageAsync(package, win64, cancellationToken).ConfigureAwait(false);
        }
        finally { InstallGate.Release(); }
    }

    private static void EnsureEmptyTarget(string win64)
    {
        if (!Directory.Exists(win64)) throw new DirectoryNotFoundException("The game's Win64 folder was not found. Choose the game folder in Settings.");
        if (Directory.Exists(Path.Combine(win64, "ue4ss")) || File.Exists(Path.Combine(win64, "dwmapi.dll"))
            || File.Exists(Path.Combine(win64, "UE4SS.dll")) || File.Exists(Path.Combine(win64, "xinput1_3.dll"))
            || File.Exists(Path.Combine(win64, "override.txt")))
            throw new IOException("An existing or incomplete mod loader was found. Setup left it unchanged. See the live-editing guide for repair steps.");
    }

    private static async Task InstallPackageAsync(Stream package, string win64, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var files = archive.Entries.Where(e => e.Name.Length > 0).ToArray();
        if (files.Length > 1000 || files.Sum(e => e.Length) > 200 * 1024 * 1024)
            throw new InvalidDataException("Unexpected live-support package size.");
        foreach (var file in files)
        {
            if (file.FullName.Contains('\\') || file.FullName.Contains(':') || file.FullName.StartsWith('/')
                || file.FullName.Split('/').Any(part => part is ".." or "."))
                throw new InvalidDataException("Invalid path in live-support package.");
        }
        string[] required = ["dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/LICENSE", "ue4ss/UE4SS-settings.ini",
            "ue4ss/Mods/shared/UEHelpers/UEHelpers.lua"];
        if (required.Any(name => files.Count(e => e.FullName == name) != 1))
            throw new InvalidDataException("The live-support package is incomplete. Setup has not changed the game.");
        var selected = files.Where(e => required.Contains(e.FullName)
            || e.FullName.StartsWith("ue4ss/Mods/shared/", StringComparison.Ordinal)
            || e.FullName.StartsWith("ue4ss/UE4SS_SDK_Backends/", StringComparison.Ordinal)).ToArray();
        EnsureEmptyTarget(win64);
        var stage = Path.Combine(win64, ".abiotic-live-setup-" + Guid.NewGuid().ToString("N"));
        var runtime = Path.Combine(win64, "ue4ss");
        var movedRuntime = false;
        try
        {
            foreach (var file in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = Path.Combine(stage, file.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                file.ExtractToFile(target);
            }
            // Install no sample/cheat mods. The app enables only its own agent afterwards.
            File.WriteAllText(Path.Combine(stage, "ue4ss", "Mods", "mods.txt"), string.Empty);
            cancellationToken.ThrowIfCancellationRequested();
            await RetryFileOperationAsync(() => Directory.Move(Path.Combine(stage, "ue4ss"), runtime), cancellationToken).ConfigureAwait(false);
            movedRuntime = true;
            // Activate the runtime last, only after all of its files are in place.
            await RetryFileOperationAsync(() => File.Move(Path.Combine(stage, "dwmapi.dll"), Path.Combine(win64, "dwmapi.dll")), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (movedRuntime) Directory.Delete(runtime, recursive: true);
            throw;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    private static async Task RetryFileOperationAsync(Action operation, CancellationToken cancellationToken)
    {
        // Windows scanners can briefly hold newly extracted DLLs open.
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { operation(); return; }
            catch (IOException ex) when (attempt < 5 && (ex.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                await Task.Delay(250 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed record Release([property: JsonPropertyName("assets")] Asset[] Assets);
    private sealed record Asset([property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string Url,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("size")] long Size);
}
