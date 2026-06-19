using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using CUE4Parse_Conversion.Textures;
using SkiaSharp;

namespace AbioticEditor.Core.Assets;

/// <summary>A world transform resolved from a cooked level: translation + rotation quaternion.</summary>
public readonly record struct ActorTransform(
    double X, double Y, double Z,
    double QuatX, double QuatY, double QuatZ, double QuatW);

/// <summary>
/// Loads Abiotic Factor's pak archives and exposes high-level asset extraction.
/// Extracted bytes are cached on disk under <see cref="CacheDirectory"/>.
/// </summary>
public sealed class GameAssetProvider : IDisposable
{
    private readonly DefaultFileProvider _provider;
    private readonly string _cacheDir;
    private readonly IReadOnlyList<string> _loadedMods;
    // CUE4Parse package loading mutates the provider's internal package cache and is not
    // guaranteed thread-safe. Icon/texture extraction runs from many fire-and-forget tasks at
    // once, so every package-load entry point serializes through this lock.
    private readonly object _providerLoadLock = new();
    private bool _disposed;

    private GameAssetProvider(DefaultFileProvider provider, string cacheDir, IReadOnlyList<string> loadedMods)
    {
        _provider = provider;
        _cacheDir = cacheDir;
        _loadedMods = loadedMods;
    }

    /// <summary>The on-disk extraction cache. Defaults to <c>%LOCALAPPDATA%/AbioticEditor/assets</c>.</summary>
    public string CacheDirectory => _cacheDir;

    /// <summary>Returns every mounted asset path. Use sparingly - there are ~50k.</summary>
    public IEnumerable<string> AssetPaths => _provider.Files.Keys;

    /// <summary>
    /// File names of the mod paks mounted from the game's <c>~mods</c>/<c>LogicMods</c>
    /// subfolders (empty when none were found or mod loading was disabled). Display-only,
    /// so the App/CLI can report what is active.
    /// </summary>
    public IReadOnlyList<string> LoadedMods => _loadedMods;

    /// <summary>
    /// True if a <c>.usmap</c> type mapping file has been registered. Required to parse
    /// <c>.uasset</c> properties (textures, materials, datatables, etc.) for UE5 shipping
    /// builds that use unversioned properties - including Abiotic Factor.
    /// </summary>
    public bool HasMappings => _provider.MappingsContainer is not null;

    /// <summary>
    /// Constructs a provider against the local AF install, or returns null if the game can't be located.
    /// <paramref name="mappingsPath"/> is an optional path to a <c>.usmap</c> file dumped from
    /// the running game (e.g. via FModel or Dumper-7). Without it only raw-byte extraction
    /// (fonts, the cooked PNGs/SVGs that ship outside .uasset wrappers) is possible.
    /// </summary>
    /// <param name="includeMods">
    /// When true (default), mod paks under the install's <c>~mods</c>/<c>LogicMods</c>
    /// subfolders are mounted too; when false only the base-game paks load. When null, the
    /// shared <see cref="ModLoadStore.ModsEnabled"/> setting (and the <c>ABIOTIC_NO_MODS</c>
    /// env var) decides.
    /// </param>
    public static GameAssetProvider? CreateForLocalInstall(string? cacheDir = null, string? mappingsPath = null, bool? includeMods = null)
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        if (paks is null)
        {
            Diagnostics.EditorLog.Warn("Assets", "Abiotic Factor install not found - asset-backed features are disabled.");
            return null;
        }

        // Fallback: look for a usmap in the conventional location.
        mappingsPath ??= FindConventionalMappings();
        try
        {
            var provider = CreateForPaks(paks, cacheDir, mappingsPath, includeMods ?? ModLoadStore.ModsEnabled);
            Diagnostics.EditorLog.Info(
                "Assets",
                $"Mounted game paks at {paks} (mappings: {(provider.HasMappings ? mappingsPath : "none - raw extraction only")}; "
                + $"mods: {(provider.LoadedMods.Count == 0 ? "none" : string.Join(", ", provider.LoadedMods))}).");
            return provider;
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Error("Assets", $"Failed to mount game paks at {paks}", ex);
            throw;
        }
    }

    /// <summary>
    /// Returns the usmap mappings file to use, or null. Resolution order:
    /// 1. <c>%LOCALAPPDATA%/AbioticEditor/mappings/Mappings.usmap</c> (user-supplied,
    ///    wins so a newer dump can override the bundled one), then
    /// 2. <c>Mappings.usmap</c> next to the executable (bundled fallback shipped with
    ///    the app).
    /// </summary>
    public static string? FindConventionalMappings()
    {
        if (File.Exists(UserMappingsPath)) return UserMappingsPath;

        var bundled = Path.Combine(AppContext.BaseDirectory, "Mappings.usmap");
        return File.Exists(bundled) ? bundled : null;
    }

    /// <summary>
    /// The user-override mappings location. A file here wins over the bundled usmap, so
    /// players on newer game builds can drop in a fresh dump without updating the editor.
    /// </summary>
    public static string UserMappingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AbioticEditor",
        "mappings",
        "Mappings.usmap");

    /// <summary>
    /// Installs a user-supplied <c>.usmap</c> into the override location
    /// (<see cref="UserMappingsPath"/>), validating the usmap magic first so a stray
    /// file can't silently break asset loading. Returns the installed path.
    /// <paramref name="targetPath"/> exists for tests only.
    /// </summary>
    public static string InstallUserMappings(string sourcePath, string? targetPath = null)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Mappings file not found.", sourcePath);

        // .usmap files start with the magic 0xC4 0x30 ("0Ä" little-endian ushort 0x30C4).
        Span<byte> magic = stackalloc byte[2];
        using (var fs = File.OpenRead(sourcePath))
        {
            if (fs.Read(magic) != 2 || magic[0] != 0xC4 || magic[1] != 0x30)
                throw new InvalidDataException(
                    $"'{Path.GetFileName(sourcePath)}' is not a valid .usmap file (bad magic). " +
                    "Export one with FModel or Dumper-7 from the game build you want to support.");
        }

        var dest = targetPath ?? UserMappingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(sourcePath, dest, overwrite: true);
        Diagnostics.EditorLog.Info("Assets", $"Installed user mappings from {sourcePath} -> {dest}.");
        return dest;
    }

    /// <summary>
    /// Constructs a provider over the given <paramref name="paksDirectory"/>. Throws if mount fails.
    /// When <paramref name="includeMods"/> is true the mod-pak subfolders (<c>~mods</c>,
    /// <c>LogicMods</c>) are mounted as well by scanning all subdirectories; otherwise only the
    /// base-game paks in the top of the directory load.
    /// </summary>
    public static GameAssetProvider CreateForPaks(string paksDirectory, string? cacheDir = null, string? mappingsPath = null, bool includeMods = true)
    {
        // Mod paks live in subfolders (~mods, LogicMods); scanning all directories mounts them.
        // Note: when a mod overrides a base asset (same package path), which copy wins depends on
        // CUE4Parse's mount order. AF/UE give ~mods higher priority; most mods only ADD content
        // (new paths) where order is irrelevant. If precise override priority is ever needed,
        // switch to mounting the base dir then each mod folder explicitly.
        var searchOption = includeMods ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var loadedMods = includeMods
            ? AfInstallLocator.FindModPaks(paksDirectory).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList()
            : new List<string>();

#pragma warning disable CS0618 // see AssetProbeTests for context on the new ctor signature
        var provider = new DefaultFileProvider(
            paksDirectory,
            searchOption,
            isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618

        provider.Initialize();

        // Unencrypted iostore still requires SubmitKey to trigger the mount step.
        // FGuid.Empty matches archives that report no encryption GUID.
        provider.SubmitKey(
            new FGuid(),
            new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        if (provider.RequiredKeys.Count > 0)
        {
            // We discovered during the probe that AF is unencrypted. If a future patch
            // ever flips this on, surface it clearly rather than silently returning empty
            // asset lists.
            var missing = string.Join(", ", provider.RequiredKeys);
            provider.Dispose();
            throw new InvalidOperationException(
                $"AF paks now require AES key(s) - missing: {missing}. Asset extraction is blocked until a key is supplied.");
        }

        var cache = cacheDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AbioticEditor",
            "assets");
        Directory.CreateDirectory(cache);

        if (mappingsPath is not null && File.Exists(mappingsPath))
        {
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath);
        }

        return new GameAssetProvider(provider, cache, loadedMods);
    }

    /// <summary>
    /// Exception thrown when a UE5-shipping asset cannot be decoded because the
    /// <c>.usmap</c> mapping file is missing.
    /// </summary>
    public sealed class MappingsRequiredException : InvalidOperationException
    {
        public MappingsRequiredException(string assetPath)
            : base($"Asset '{assetPath}' uses unversioned properties; a Mappings.usmap file must be registered via GameAssetProvider.CreateFor*(mappingsPath: ...).") { }
    }

    /// <summary>
    /// Extracts a UTexture2D to a PNG on disk and returns the cached path. Subsequent calls
    /// hit the cache. <paramref name="assetPath"/> is the package path without extension
    /// (e.g. <c>AbioticFactor/Content/Textures/GUI/Logos/ABF-Full-Color-1024w</c>).
    /// </summary>
    public string? ExtractTextureAsPng(string assetPath)
    {
        ThrowIfDisposed();

        var cachePath = Path.Combine(_cacheDir, "textures", assetPath.Replace('/', Path.DirectorySeparatorChar) + ".png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        if (!HasMappings)
        {
            throw new MappingsRequiredException(assetPath);
        }

        var texture = LoadFirstTexture(assetPath);
        if (texture is null) return null;

        var decoded = texture.Decode(ETexturePlatform.DesktopMobile);
        if (decoded is null)
        {
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        // CUE4Parse master returns its own CTexture wrapper (Width/Height/Data). Copy the
        // RGBA buffer into the image rather than pointing at the managed array - a pinned
        // pointer must not outlive its fixed scope, and SKImage.FromPixelCopy avoids the
        // pinning question entirely.
        var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var image = SKImage.FromPixelCopy(info, decoded.Data);
        if (image is null) return null;
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        // Two tasks can extract the same icon at once (File.Create is exclusive, so a naive
        // write would throw a sharing violation on the loser). Write to a unique temp then
        // atomically publish; if another task published first, keep theirs.
        var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temp))
            {
                data.SaveTo(stream);
            }
            try
            {
                File.Move(temp, cachePath, overwrite: false);
            }
            catch (IOException) when (File.Exists(cachePath))
            {
                TryDeleteFile(temp);
            }
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }

        return cachePath;
    }

    /// <summary>
    /// Extracts a texture given a UE-style object reference like
    /// <c>/Game/Textures/GUI/ItemIcons/foo.foo</c>. Translates the <c>/Game/...</c> prefix
    /// to AF's content root and strips the duplicate object-name suffix.
    /// </summary>
    public string? ExtractTextureByGameRef(string? gameRef)
    {
        if (string.IsNullOrEmpty(gameRef)) return null;

        // "/Game/Foo/Bar.Bar" -> "AbioticFactor/Content/Foo/Bar"
        var path = gameRef;
        if (path.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase))
        {
            path = "AbioticFactor/Content/" + path["/Game/".Length..];
        }
        var dot = path.LastIndexOf('.');
        var slash = path.LastIndexOf('/');
        if (dot > slash)
        {
            path = path[..dot];
        }
        return ExtractTextureAsPng(path);
    }

    private UTexture2D? LoadFirstTexture(string assetPath)
    {
        // Try the conventional object path first: `path/Name.Name`.
        var assetName = Path.GetFileName(assetPath);
        var objectPath = $"{assetPath}.{assetName}";
        lock (_providerLoadLock)
        {
            if (_provider.TryLoadPackageObject<UTexture2D>(objectPath, out var direct))
            {
                return direct;
            }

            // Fall back to enumerating all exports in the package and returning the first
            // UTexture2D we find. Some assets use a non-matching export name.
            if (_provider.TryLoadPackage(assetPath, out var package))
            {
                foreach (var export in package.GetExports())
                {
                    if (export is UTexture2D tex) return tex;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves whatever <c>.ufont</c> file is associated with the given font asset path
    /// (e.g. <c>AbioticFactor/Content/Blueprints/Widgets/Fonts/digital-7</c>) and writes it to
    /// the cache as a usable .ttf. Returns the cached path, or null if it can't be resolved.
    /// </summary>
    public string? ExtractFontAsTtf(string assetPath)
    {
        ThrowIfDisposed();

        var cachePath = Path.Combine(_cacheDir, "fonts", Path.GetFileName(assetPath) + ".ttf");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        // UE wraps fonts in a .uasset/.uexp pair plus an actual font payload (.ufont).
        // For the digital-7 family the .ufont sits alongside the .uasset; in other layouts
        // the font bytes live inside the uasset itself. Probe both.
        var ufontKey = assetPath + ".ufont";
        if (_provider.Files.TryGetValue(ufontKey, out var ufontFile))
        {
            var bytes = ufontFile.Read();
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            File.WriteAllBytes(cachePath, bytes);
            return cachePath;
        }

        return null;
    }

    /// <summary>Reads any file from the mounted paks by its full asset path.</summary>
    public byte[]? ReadRawFile(string fullAssetPath)
    {
        ThrowIfDisposed();
        return _provider.Files.TryGetValue(fullAssetPath, out var file) ? file.Read() : null;
    }

    /// <summary>
    /// Loads a UE package by path. Throws via CUE4Parse if mappings are missing for
    /// shipping-build packages that use unversioned properties. Intended for callers
    /// that already check <see cref="HasMappings"/>.
    /// </summary>
    internal CUE4Parse.UE4.Assets.IPackage LoadPackageInternal(string packagePath)
    {
        ThrowIfDisposed();
        lock (_providerLoadLock)
        {
            return _provider.LoadPackage(packagePath);
        }
    }

    /// <summary>
    /// Loads a <see cref="CUE4Parse.UE4.Assets.Exports.Engine.UDataTable"/> export from the given package path, or null if the
    /// package isn't a data table / can't be loaded. Thread-safe (serializes through the
    /// package-load lock). Used by <see cref="ModTableDiscovery"/> to probe candidate tables.
    /// </summary>
    internal CUE4Parse.UE4.Assets.Exports.Engine.UDataTable? TryLoadDataTable(string packagePath)
    {
        ThrowIfDisposed();
        lock (_providerLoadLock)
        {
            if (!_provider.TryLoadPackage(packagePath, out var package)) return null;
            foreach (var export in package.GetExports())
            {
                if (export is CUE4Parse.UE4.Assets.Exports.Engine.UDataTable dt) return dt;
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a placed actor's world transform from a cooked level package - used to find a
    /// vehicle's original spawn position (the <c>VehicleSpawn_*</c> actor named by its save key).
    /// Returns translation (X,Y,Z) and rotation as a quaternion (X,Y,Z,W), or null when the
    /// actor / level / mappings can't be resolved (graceful: callers disable "reset to spawn").
    /// <paramref name="actorObjectPath"/> is the full object path, e.g.
    /// <c>/Game/Maps/Facility_MFWest.Facility_MFWest:PersistentLevel.VehicleSpawn_Forklift_C_3</c>.
    /// </summary>
    public ActorTransform? TryGetActorTransform(string? actorObjectPath)
    {
        if (string.IsNullOrEmpty(actorObjectPath) || _disposed) return null;
        try
        {
            CUE4Parse.UE4.Assets.Exports.UObject? actor;
            lock (_providerLoadLock)
            {
                if (!_provider.TryLoadPackageObject(actorObjectPath, out actor) || actor is null)
                {
                    return null;
                }
            }

            // The transform lives on the actor's RootComponent (a scene component export).
            var root = actor.GetOrDefault<CUE4Parse.UE4.Assets.Exports.UObject?>("RootComponent");
            var holder = root ?? actor;

            var loc = holder.GetOrDefault<CUE4Parse.UE4.Objects.Core.Math.FVector>("RelativeLocation");
            var rot = holder.GetOrDefault<CUE4Parse.UE4.Objects.Core.Math.FRotator>("RelativeRotation");
            var q = rot.Quaternion();
            return new ActorTransform(loc.X, loc.Y, loc.Z, q.X, q.Y, q.Z, q.W);
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("Assets", $"Could not resolve spawn transform for {actorObjectPath}: {ex.Message}");
            return null;
        }
    }

    private static void TryDeleteFile(string path)
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
            // Best-effort cleanup of a temp extraction file; nothing actionable if it lingers.
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _provider.Dispose();
    }
}
