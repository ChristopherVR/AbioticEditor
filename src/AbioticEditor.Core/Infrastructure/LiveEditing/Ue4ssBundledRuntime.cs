using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace AbioticEditor.Core.LiveEditing;

/// <summary>The pinned UE4SS package a Windows release may bundle, as recorded in
/// <c>live-agent/ue4ss/runtime.json</c> (see <see cref="Ue4ssBundledRuntime.TryLoad"/>).</summary>
public sealed record Ue4ssRuntimeManifest(
    string Name,
    string Version,
    string Asset,
    string Sha256,
    long Size,
    string License,
    string Source)
{
    /// <summary>Checksums of unpacked runtime files, relative to the bundle's files folder.</summary>
    public Dictionary<string, string>? Files { get; init; }
}

/// <summary>
/// Installs the UE4SS package this Windows release bundles, once the player has consented.
/// GitHub packages carry the original archive; Windows Nexus packages carry unpacked files with
/// individual SHA-256 checksums in the manifest. Both install without network access and
/// verify their contents before touching the game folder.
///
/// <para>Only the runtime itself and its shared support files are installed
/// (<c>ue4ss/Mods/shared/**</c> and <c>ue4ss/UE4SS_SDK_Backends/**</c>); the sample/cheat mods the
/// upstream package ships (console/cheat-manager enablers, split-screen, line-trace, and friends)
/// are deliberately left out of the extraction allow-list. This mirrors the manual setup guide,
/// which only ever asks a player to keep the runtime + shared files, and avoids silently turning
/// on gameplay-altering mods nobody asked for.</para>
/// </summary>
public sealed class Ue4ssBundledRuntime
{
    // Serializes concurrent installs the same way the removed network installer did: two callers
    // racing to set up live editing must not both extract into the same Win64 folder at once.
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private Ue4ssBundledRuntime(Ue4ssRuntimeManifest manifest, string packagePath)
    {
        Manifest = manifest;
        PackagePath = packagePath;
    }

    public Ue4ssRuntimeManifest Manifest { get; }
    /// <summary>Path to the original archive or the unpacked runtime files directory.</summary>
    public string PackagePath { get; }

    /// <summary>
    /// Returns the bundled runtime described by <c>runtime.json</c> and either <c>UE4SS.zip</c> or <c>files/</c> in
    /// <paramref name="bundleDirectory"/>, or null when either file is missing (a dev build of the
    /// editor itself, or a platform this release never bundles UE4SS for) or the manifest cannot
    /// be parsed. Never touches the package's contents; call <see cref="InstallAsync"/> to verify
    /// and install it.
    /// </summary>
    public static Ue4ssBundledRuntime? TryLoad(string bundleDirectory)
    {
        var manifestPath = Path.Combine(bundleDirectory, "runtime.json");
        var packagePath = Path.Combine(bundleDirectory, "UE4SS.zip");
        if (!File.Exists(manifestPath)) return null;

        try
        {
            var manifest = JsonSerializer.Deserialize<Ue4ssRuntimeManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest is null) return null;
            if (manifest.Files is not null)
                return Directory.Exists(Path.Combine(bundleDirectory, "files"))
                    ? new Ue4ssBundledRuntime(manifest, Path.Combine(bundleDirectory, "files")) : null;
            return File.Exists(packagePath) ? new Ue4ssBundledRuntime(manifest, packagePath) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a standard UE4SS installation (bundled or manually installed by the
    /// player) is already present, without changing anything.</summary>
    public static bool IsInstalled(string win64) => Ue4ssInstallation.FindModsDirectory(win64) is not null;

    /// <summary>
    /// Installs the bundled package into <paramref name="win64"/>. No-ops if UE4SS is already
    /// installed. Verifies the archive size and SHA-256, or each unpacked file checksum, against <see cref="Manifest"/>
    /// before writing anything to <paramref name="win64"/>, so a corrupted or tampered release
    /// download is caught (<see cref="InvalidDataException"/>) without leaving the game folder in
    /// a partial state. Never overwrites an existing mod-loader install; see
    /// <see cref="EnsureEmptyTarget"/>.
    /// </summary>
    public async Task InstallAsync(string win64, CancellationToken cancellationToken = default)
    {
        await InstallGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsInstalled(win64)) return;
            EnsureEmptyTarget(win64);
            if (Manifest.Files is not null)
            {
                // Build a verified in-memory snapshot before touching the game. Installation uses
                // the same allow-list and activation order as the original archive layout.
                using var snapshot = await SnapshotFilesAsync(cancellationToken).ConfigureAwait(false);
                await InstallPackageAsync(snapshot, win64, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await VerifyPackageAsync(cancellationToken).ConfigureAwait(false);
                await using var package = File.OpenRead(PackagePath);
                await InstallPackageAsync(package, win64, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private async Task<MemoryStream> SnapshotFilesAsync(CancellationToken cancellationToken)
    {
        var files = Manifest.Files!;
        if (files.Count is 0 or > 1000)
            throw new InvalidDataException("Unexpected bundled UE4SS file count.");
        var snapshot = new MemoryStream();
        try
        {
            using (var archive = new ZipArchive(snapshot, ZipArchiveMode.Create, leaveOpen: true))
            {
                long total = 0;
                foreach (var (name, expectedHash) in files)
                {
                    if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':')
                        || name.StartsWith('/') || name.Split('/').Any(part => part is ".." or "." or ""))
                        throw new InvalidDataException("Invalid path in bundled UE4SS manifest.");
                    var source = Path.Combine(PackagePath, name);
                    for (var current = source; current is not null; current = Path.GetDirectoryName(current))
                    {
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                            throw new InvalidDataException("Linked files are not allowed in the bundled UE4SS runtime.");
                        if (current.Equals(PackagePath, StringComparison.OrdinalIgnoreCase)) break;
                    }
                    await using var input = File.OpenRead(source);
                    total += input.Length;
                    if (total > 200 * 1024 * 1024)
                        throw new InvalidDataException("Unexpected bundled UE4SS package size.");
                    using var content = new MemoryStream();
                    await input.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
                    var hash = Convert.ToHexString(SHA256.HashData(content.GetBuffer().AsSpan(0, (int)content.Length)));
                    if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("A bundled UE4SS file did not match its expected checksum. Reinstall the editor and try again.");
                    content.Position = 0;
                    await using var output = archive.CreateEntry(name, CompressionLevel.NoCompression).Open();
                    await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }
            }
            snapshot.Position = 0;
            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    private async Task VerifyPackageAsync(CancellationToken cancellationToken)
    {
        var info = new FileInfo(PackagePath);
        if (!info.Exists || info.Length != Manifest.Size)
            throw new InvalidDataException(
                $"The bundled UE4SS package ({Manifest.Version}) is not the size this release expects. Reinstall the editor and try again.");

        await using var package = File.OpenRead(PackagePath);
        var hash = await SHA256.HashDataAsync(package, cancellationToken).ConfigureAwait(false);
        var digest = Convert.ToHexString(hash);
        if (!digest.Equals(Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"The bundled UE4SS package ({Manifest.Version}) did not match its expected checksum. Reinstall the editor and try again.");
    }

    /// <summary>
    /// Refuses to write anything when the target already carries ANY trace of a mod loader -
    /// a complete UE4SS install, a partial one, or a different loader that also uses these file
    /// names. An existing install (with the player's own settings and mods) must never be
    /// silently overwritten.
    /// </summary>
    private static void EnsureEmptyTarget(string win64)
    {
        if (!Directory.Exists(win64))
            throw new DirectoryNotFoundException("The folder the game runs from was not found. Choose the game folder again.");
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
            throw new InvalidDataException("Unexpected bundled UE4SS package size.");
        foreach (var file in files)
        {
            if (file.FullName.Contains('\\') || file.FullName.Contains(':') || file.FullName.StartsWith('/')
                || file.FullName.Split('/').Any(part => part is ".." or "."))
                throw new InvalidDataException("Invalid path in bundled UE4SS package.");
        }

        string[] required =
        [
            "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/LICENSE", "ue4ss/UE4SS-settings.ini",
            "ue4ss/Mods/shared/UEHelpers/UEHelpers.lua",
        ];
        if (required.Any(name => files.Count(e => e.FullName == name) != 1))
            throw new InvalidDataException("The bundled UE4SS package is incomplete. Setup has not changed the game.");

        // No sample/cheat mods and no unrelated SDK backend extras - only the runtime itself and
        // the shared support files every mod (including this editor's own) may need.
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
            // No sample/cheat mods are enabled. The app enables only its own agent afterwards.
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
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }

    private static async Task RetryFileOperationAsync(Action operation, CancellationToken cancellationToken)
    {
        // Windows scanners can briefly hold newly extracted DLLs open.
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                operation();
                return;
            }
            catch (IOException ex) when (attempt < 5 && (ex.HResult & 0xFFFF) is 5 or 32 or 33)
            {
                await Task.Delay(250 * (attempt + 1), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
