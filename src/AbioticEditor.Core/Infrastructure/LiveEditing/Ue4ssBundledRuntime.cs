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
    string Source);

/// <summary>
/// Installs the UE4SS package this Windows release bundles, once the player has consented.
/// Unlike the download-from-GitHub approach this replaced, the package ships inside the editor's
/// own release archive (<c>live-agent/ue4ss/UE4SS.zip</c> next to its pin manifest), so installing
/// it needs no network access at runtime: nothing is fetched, only the SHA-256 the editor's own
/// release process already verified is checked again here before any file is touched. That keeps
/// this in line with the rest of the app, which never talks to the network to edit a save, and
/// means live setup works the same offline as it does connected.
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
    public string PackagePath { get; }

    /// <summary>
    /// Returns the bundled runtime described by <c>runtime.json</c> and <c>UE4SS.zip</c> in
    /// <paramref name="bundleDirectory"/>, or null when either file is missing (a dev build of the
    /// editor itself, or a platform this release never bundles UE4SS for) or the manifest cannot
    /// be parsed. Never touches the package's contents; call <see cref="InstallAsync"/> to verify
    /// and install it.
    /// </summary>
    public static Ue4ssBundledRuntime? TryLoad(string bundleDirectory)
    {
        var manifestPath = Path.Combine(bundleDirectory, "runtime.json");
        var packagePath = Path.Combine(bundleDirectory, "UE4SS.zip");
        if (!File.Exists(manifestPath) || !File.Exists(packagePath)) return null;

        try
        {
            var manifest = JsonSerializer.Deserialize<Ue4ssRuntimeManifest>(File.ReadAllText(manifestPath), JsonOptions);
            return manifest is null ? null : new Ue4ssBundledRuntime(manifest, packagePath);
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
    /// installed. Verifies the bundled ZIP's size and SHA-256 against <see cref="Manifest"/>
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
            await VerifyPackageAsync(cancellationToken).ConfigureAwait(false);
            await using var package = File.OpenRead(PackagePath);
            await InstallPackageAsync(package, win64, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            InstallGate.Release();
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
            throw new DirectoryNotFoundException("The game's Win64 folder was not found. Choose the game folder in Settings.");
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
