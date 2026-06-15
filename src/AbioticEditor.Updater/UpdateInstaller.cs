using System.IO.Compression;
using System.Net.Http.Headers;

namespace AbioticEditor.Updater;

/// <summary>
/// Downloads a release asset and applies it over the current install. Splitting download
/// from apply lets a UI show a progress bar, confirm, then trigger the (process-ending)
/// replace as a separate step.
/// </summary>
public sealed class UpdateInstaller
{
    private readonly UpdaterOptions _options;
    private readonly HttpClient _http;
    private readonly IUpdaterLog _log;

    public UpdateInstaller(UpdaterOptions options, HttpClient? http = null, IUpdaterLog? log = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? new HttpClient();
        _log = log ?? IUpdaterLog.Null;
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into the working folder for <paramref name="tag"/>
    /// and, if it is an archive, extracts it. Returns a <see cref="StagedUpdate"/> describing
    /// the folder whose contents should replace the install. <paramref name="progress"/>
    /// reports 0..1 download fraction (only when the server sends a content length).
    /// </summary>
    public async Task<StagedUpdate> DownloadAsync(
        ReleaseAsset asset,
        string tag,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var workingDir = UpdatePaths.WorkingDirectoryFor(tag);

        // The asset name comes from the release JSON; reduce it to a bare file name so a crafted
        // name like "..\..\evil.zip" can't write outside the working folder (zip-slip's sibling).
        var safeName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            throw new UpdaterException($"release asset has an invalid name: '{asset.Name}'.");
        }
        var downloadPath = Path.Combine(workingDir, safeName);

        _log.Info($"Downloading {safeName} -> {downloadPath}");
        await DownloadFileAsync(asset.DownloadUrl, downloadPath, asset.Size, progress, cancellationToken)
            .ConfigureAwait(false);

        var stagedDir = Path.Combine(workingDir, "staged");
        if (Directory.Exists(stagedDir))
        {
            Directory.Delete(stagedDir, recursive: true);
        }
        Directory.CreateDirectory(stagedDir);

        if (IsZip(safeName))
        {
            _log.Info($"Extracting archive to {stagedDir}");
            ZipFile.ExtractToDirectory(downloadPath, stagedDir, overwriteFiles: true);
            FlattenSingleRoot(stagedDir);
        }
        else
        {
            // A bare installer/executable: stage the file as-is for the host to run/copy.
            var dest = Path.Combine(stagedDir, safeName);
            File.Copy(downloadPath, dest, overwrite: true);
        }

        return new StagedUpdate(tag, workingDir, stagedDir, downloadPath, safeName);
    }

    /// <summary>
    /// Replaces the current install with the staged files entirely in managed code (see
    /// <see cref="InPlaceReplacer"/> - no batch/shell script and no elevation beyond write
    /// access to the install folder), optionally relaunches, and returns. <b>The caller must
    /// terminate the process immediately afterwards</b> (the host's responsibility, e.g.
    /// <c>Environment.Exit</c> or app shutdown) so the swapped-in files take effect.
    /// </summary>
    /// <param name="staged">The download produced by <see cref="DownloadAsync"/>.</param>
    /// <param name="relaunch">When true, the app is restarted after the swap.</param>
    /// <param name="installDirectory">Target to overwrite; defaults to the running install dir.</param>
    /// <param name="relaunchPath">Executable to relaunch; defaults to the current process exe.</param>
    public void ApplyAndExit(
        StagedUpdate staged,
        bool relaunch = true,
        string? installDirectory = null,
        string? relaunchPath = null)
    {
        ArgumentNullException.ThrowIfNull(staged);

        if (staged.IsBareInstaller)
        {
            // The asset is a self-contained installer (.msi/.exe/.pkg): hand control to it
            // rather than file-copying. It manages its own replace + relaunch.
            LaunchInstaller(staged);
            return;
        }

        var install = installDirectory ?? UpdatePaths.CurrentInstallDirectory();
        _log.Info($"Applying update {staged.Tag}: {staged.StagedDirectory} -> {install}");

        var deferred = InPlaceReplacer.Apply(staged.StagedDirectory, install, _log);
        if (deferred.Count > 0)
        {
            _log.Warn($"{deferred.Count} file(s) will be finished on next startup.");
        }

        if (relaunch)
        {
            var target = relaunchPath ?? CurrentExecutablePath();
            Relaunch(target);
        }
    }

    /// <summary>Starts the freshly installed executable as an independent process.</summary>
    private void Relaunch(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            _log.Warn($"Cannot relaunch - executable not found: {executablePath}");
            return;
        }
        try
        {
            _log.Info($"Relaunching {executablePath}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            _log.Error("Relaunch failed; please start the app manually.", ex);
        }
    }

    private void LaunchInstaller(StagedUpdate staged)
    {
        var installerPath = Path.Combine(staged.StagedDirectory, staged.AssetName);
        _log.Info($"Launching bundled installer: {installerPath}");
        var psi = new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true };
        System.Diagnostics.Process.Start(psi);
    }

    private async Task DownloadFileAsync(
        string url, string destination, long expectedSize, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AbioticEditor", "1.0"));
        }

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdaterException(
                $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase} for {url}");
        }

        var total = response.Content.Headers.ContentLength;
        long readTotal = 0;

        // Stream to disk inside an explicit scope so the file handle is closed before any
        // verification/cleanup below (FileShare.None would otherwise block the delete).
        await using (var source = await response.Content
            .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readTotal += read;
                if (total is > 0)
                {
                    progress?.Report((double)readTotal / total.Value);
                }
            }
        }

        // Verify the whole asset arrived. A truncated/substituted body (dropped connection,
        // captive portal, proxy) must not be extracted and applied over the install.
        if (expectedSize > 0 && readTotal != expectedSize)
        {
            TryDelete(destination);
            throw new UpdaterException(
                $"Download incomplete for {url}: expected {expectedSize} bytes but received {readTotal}.");
        }

        progress?.Report(1.0);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the partial download.
        }
    }

    private static bool IsZip(string name)
        => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// If the archive extracted to a single wrapper folder (e.g. <c>AbioticEditor-1.4.2/</c>),
    /// hoist its contents up so <c>stagedDir</c> directly mirrors the install layout.
    /// </summary>
    private static void FlattenSingleRoot(string stagedDir)
    {
        var entries = Directory.GetFileSystemEntries(stagedDir);
        if (entries.Length != 1 || !Directory.Exists(entries[0]))
        {
            return;
        }

        var inner = entries[0];
        foreach (var dir in Directory.GetDirectories(inner))
        {
            var dest = Path.Combine(stagedDir, Path.GetFileName(dir));
            Directory.Move(dir, dest);
        }
        foreach (var file in Directory.GetFiles(inner))
        {
            var dest = Path.Combine(stagedDir, Path.GetFileName(file));
            File.Move(file, dest);
        }
        Directory.Delete(inner, recursive: true);
    }

    private static string CurrentExecutablePath()
    {
        // The host .exe (not the dotnet muxer). Environment.ProcessPath is the OS process image
        // path and is correct for framework-dependent, self-contained, and single-file publishes
        // alike. Assembly.Location is empty in a single-file app (IL3000), so fall back to the
        // app base directory + entry-assembly name instead.
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            return processPath;
        }
        var entryName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
        return entryName is null ? string.Empty : Path.Combine(AppContext.BaseDirectory, entryName + ".exe");
    }
}

/// <summary>The result of staging a download: where the new files are ready to be applied.</summary>
public sealed class StagedUpdate(
    string tag, string workingDirectory, string stagedDirectory, string downloadPath, string assetName)
{
    public string Tag { get; } = tag;

    public string WorkingDirectory { get; } = workingDirectory;

    /// <summary>Folder whose contents mirror the install layout (or hold the bare installer).</summary>
    public string StagedDirectory { get; } = stagedDirectory;

    public string DownloadPath { get; } = downloadPath;

    public string AssetName { get; } = assetName;

    /// <summary>True when the asset is a self-contained installer rather than a file archive.</summary>
    public bool IsBareInstaller =>
        AssetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
        || AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || AssetName.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)
        || AssetName.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase);
}
