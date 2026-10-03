using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
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
public sealed partial class GameAssetProvider : IDisposable
{
    private readonly DefaultFileProvider _provider;
    private readonly string _cacheDir;
    private readonly IReadOnlyList<string> _loadedMods;
    // CUE4Parse package loading mutates the provider's internal package cache and is not
    // guaranteed thread-safe. Icon/texture extraction runs from many fire-and-forget tasks at
    // once, so every package-load entry point serializes through this lock.
    private readonly object _providerLoadLock = new();
    // How many threads are queued for the lock, and how deep this thread holds it: a long read can
    // step aside for them (see YieldToWaitingReaders).
    private int _providerWaiters;
    [ThreadStatic] private static int t_providerDepth;
    private string? _paksDirectory;
    private bool _disposed;

    private GameAssetProvider(DefaultFileProvider provider, string cacheDir, IReadOnlyList<string> loadedMods)
    {
        _provider = provider;
        _cacheDir = cacheDir;
        _loadedMods = loadedMods;
    }

    /// <summary>The on-disk extraction cache. Defaults to <c>%LOCALAPPDATA%/AbioticEditor/assets</c>.</summary>
    public string CacheDirectory => _cacheDir;

    /// <summary>
    /// Runs <paramref name="read"/> against the underlying CUE4Parse provider while holding the
    /// provider's load lock (package loading is not thread-safe). For callers, such as plugins,
    /// that need game data this class has no dedicated method for. Keep the work inside short:
    /// every other extraction waits on the same lock.
    /// </summary>
    public T UseFileProvider<T>(Func<CUE4Parse.FileProvider.IFileProvider, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        ThrowIfDisposed();
        using (ProviderLock())
        {
            return read(_provider);
        }
    }

    /// <summary>
    /// For a long read inside <see cref="UseFileProvider{T}"/> (a whole level, say): when other
    /// reads are waiting for the game files, lets them all go first and then carries on. Call it
    /// between steps that keep no half-read state in the provider. Does nothing when nobody waits
    /// or when the calling thread is not holding the files exactly once.
    /// </summary>
    public void YieldToWaitingReaders()
    {
        if (Volatile.Read(ref _providerWaiters) == 0 || t_providerDepth != 1 || !Monitor.IsEntered(_providerLoadLock)) return;
        Monitor.Exit(_providerLoadLock);
        try
        {
            while (Volatile.Read(ref _providerWaiters) > 0) Thread.Sleep(1);
        }
        finally
        {
            Monitor.Enter(_providerLoadLock);
        }
    }

    private ProviderLease ProviderLock()
    {
        Interlocked.Increment(ref _providerWaiters);
        try { Monitor.Enter(_providerLoadLock); }
        finally { Interlocked.Decrement(ref _providerWaiters); }
        t_providerDepth++;
        return new ProviderLease(_providerLoadLock);
    }

    private readonly struct ProviderLease(object gate) : IDisposable
    {
        public void Dispose()
        {
            t_providerDepth--;
            Monitor.Exit(gate);
        }
    }

    /// <summary>Returns every mounted asset path. Use sparingly - there are ~50k.</summary>
    public IEnumerable<string> AssetPaths => _provider.Files.Keys;

    private static readonly System.Text.RegularExpressions.Regex GameLocresCulturePattern = new(
        @"^AbioticFactor/Content/Localization/Game/([^/]+)/[^/]+\.locres$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// The culture codes the game itself ships translated text for (read live from the mounted
    /// paks' <c>Content/Localization/Game/&lt;culture&gt;/Game.locres</c> files), e.g.
    /// <c>de, en, es-419, fr, ja, pt-BR, ru, zh-Hans, zh-Hant</c>. Independent of which culture
    /// (if any) was requested via <c>CreateFor*(culture:)</c> - the locres files are mounted
    /// regardless of which one got loaded into <see cref="GameLocalizationLoader"/>. Used to
    /// populate a "game data language" picker without hardcoding a list that could go stale on a
    /// future game patch.
    /// </summary>
    public IReadOnlyList<string> DiscoverAvailableCultures()
    {
        ThrowIfDisposed();
        var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in _provider.Files.Keys)
        {
            var match = GameLocresCulturePattern.Match(path);
            if (match.Success)
            {
                set.Add(match.Groups[1].Value);
            }
        }
        return set.ToList();
    }

    /// <summary>
    /// Names of the mods actually mounted from the game's <c>~mods</c>/<c>LogicMods</c> subfolders
    /// (empty when none are present, mods are disabled, or each was individually turned off).
    /// Display-only, so the App/CLI can report what is active.
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
    /// <param name="culture">
    /// A game-shipped culture code (e.g. <c>"ru"</c>) to load item/trait/skill/recipe display
    /// text in, or null/<c>"en"</c> to leave the baked-in English text as-is. See
    /// <see cref="GameLocalizationLoader"/> for how this differs from CUE4Parse's own
    /// (non-working, for this game) <c>ChangeCulture</c>.
    /// </param>
    public static GameAssetProvider? CreateForLocalInstall(string? cacheDir = null, string? mappingsPath = null, bool? includeMods = null, string? culture = null)
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
            var provider = CreateForPaks(paks, cacheDir, mappingsPath, includeMods ?? ModLoadStore.ModsEnabled, culture);
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
    /// Returns the usmap mappings file to use, or null: the newer of
    /// <c>%LOCALAPPDATA%/AbioticEditor/mappings/Mappings.usmap</c> (user-supplied, for a game build
    /// newer than the editor) and <c>Mappings.usmap</c> next to the executable (bundled with the
    /// app), by last-write time. The user file used to win outright, so a dump imported once kept
    /// shadowing every newer bundled file after editor updates (the 2026-05 dump hid the 2026-10
    /// one, which knows <c>PlantData.SeedItem</c>).
    /// </summary>
    public static string? FindConventionalMappings()
        => FindConventionalMappings(UserMappingsPath, Path.Combine(AppContext.BaseDirectory, "Mappings.usmap"));

    /// <summary>The newer of two candidate mappings files (either may be missing); see the parameterless overload.</summary>
    public static string? FindConventionalMappings(string userPath, string bundledPath)
    {
        var user = File.Exists(userPath);
        var bundled = File.Exists(bundledPath);
        if (user && bundled)
            return File.GetLastWriteTimeUtc(userPath) >= File.GetLastWriteTimeUtc(bundledPath) ? userPath : bundledPath;
        return user ? userPath : bundled ? bundledPath : null;
    }

    /// <summary>
    /// The user-override mappings location. A file here wins over the bundled usmap while it is
    /// the newer of the two, so players on newer game builds can drop in a fresh dump without
    /// updating the editor, and a later editor update with a fresher bundled file still takes over.
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
    /// When <paramref name="includeMods"/> is true, individually-enabled mod paks from the
    /// <c>~mods</c>/<c>LogicMods</c> subfolders are mounted on top of the base game; otherwise only
    /// the base-game paks load. Per-mod enablement is read from <see cref="ModLoadStore"/>.
    /// </summary>
    /// <param name="culture">See <see cref="CreateForLocalInstall"/>. Applied right after mount,
    /// before any caller can load a DataTable package - required, since <c>FText</c> resolves its
    /// localized string once, at deserialize time.</param>
    public static GameAssetProvider CreateForPaks(string paksDirectory, string? cacheDir = null, string? mappingsPath = null, bool includeMods = true, string? culture = null)
    {
#pragma warning disable CS0618 // see AssetProbeTests for context on the new ctor signature
        // Mount only the base-game paks from the top of the directory; mod paks live in subfolders
        // and are registered explicitly below so individual mods can be turned off.
        var provider = new DefaultFileProvider(
            paksDirectory,
            SearchOption.TopDirectoryOnly,
            isCaseInsensitive: true,
            new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618

        provider.Initialize();

        // Register each enabled mod's paks AFTER the base game so, when a mod overrides a base asset
        // (same package path), the mod is mounted last and wins. Most mods only ADD content (new
        // paths) where order is irrelevant. A mod whose pak fails to register is skipped, not fatal.
        var loadedMods = new List<string>();
        if (includeMods)
        {
            foreach (var mod in AfInstallLocator.FindMods(paksDirectory))
            {
                if (!ModLoadStore.IsModEnabled(mod.Name)) continue;
                var any = false;
                foreach (var file in mod.Files)
                {
                    try
                    {
                        provider.RegisterVfs(file);
                        any = true;
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.EditorLog.Warn("Assets", $"Failed to register mod pak '{file}': {ex.Message}");
                    }
                }
                if (any) loadedMods.Add(mod.Name);
            }
        }

        // Unencrypted iostore still requires SubmitKey to trigger the mount step (mounts the base
        // paks plus every mod pak registered above). FGuid.Empty matches archives with no GUID.
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

        // Must happen before any caller loads a DataTable package - FText resolves its
        // LocalizedString once, at deserialize time, not on later lookup.
        GameLocalizationLoader.Apply(provider, culture);

        return new GameAssetProvider(provider, cache, loadedMods) { _paksDirectory = paksDirectory };
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
        using (ProviderLock())
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
        using (ProviderLock())
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
        using (ProviderLock())
        {
            if (!_provider.TryLoadPackage(packagePath, out var package)) return null;
            foreach (var export in package.GetExports())
            {
                if (export is CUE4Parse.UE4.Assets.Exports.Engine.UDataTable dt) return dt;
            }
        }
        return null;
    }

    private Dictionary<string, string>? _narrativeNames;
    public string? TryGetNarrativeCharacterName(string actorPath)
    {
        if (_disposed) return null;
        using (ProviderLock())
        {
            if (!_provider.TryLoadPackageObject(actorPath, out var actor) || actor is null) return null;
            var properties = Newtonsoft.Json.Linq.JObject.FromObject(actor)["Properties"];
            var row = (string?)properties?["NarrativeNPC_ConversationRow"]?["RowName"];
            if (string.IsNullOrEmpty(row)) return null;
            if (_narrativeNames is null)
            {
                var table = TryLoadDataTable("AbioticFactor/Content/Blueprints/DataTables/DT_NPC_Conversations");
                if (table is null) return null;
                var rows = Newtonsoft.Json.Linq.JObject.FromObject(table)["Rows"] as Newtonsoft.Json.Linq.JObject;
                _narrativeNames = rows?.Properties().ToDictionary(p => p.Name,
                    p => (string?)p.Value["NPCName"]?["LocalizedString"] ?? "", StringComparer.OrdinalIgnoreCase) ?? [];
            }
            return _narrativeNames.TryGetValue(row, out var name) && !string.IsNullOrWhiteSpace(name) ? name : null;
        }
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
        if (TryGetKnownActorPosition(actorObjectPath, out var known)) return known;
        try
        {
            ActorTransform? found = null;
            using (ProviderLock())
            {
                // Read under the lock: the actor's properties load more of its level file lazily.
                if (LoadActorKeepingLevel(actorObjectPath) is { } actor)
                {
                    // The transform lives on the actor's RootComponent (a scene component export).
                    var root = actor.GetOrDefault<CUE4Parse.UE4.Assets.Exports.UObject?>("RootComponent");
                    var holder = root ?? actor;

                    var matrix = ComponentPlacement(holder, new HashSet<CUE4Parse.UE4.Assets.Exports.UObject>());
                    System.Numerics.Matrix4x4.Decompose(matrix, out _, out var q, out var loc);
                    found = new ActorTransform(loc.X, loc.Y, loc.Z, q.X, q.Y, q.Z, q.W);
                }
            }
            RememberActorPosition(actorObjectPath, found);
            return found;
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("Assets", $"Could not resolve spawn transform for {actorObjectPath}: {ex.Message}");
            return null;
        }
    }

    // Child-actor roots (including recall buttons) attach to another actor's component.
    // A root with no explicit RelativeLocation is not necessarily at the level origin.
    // Cooked instance properties also omit component offsets unchanged from their template.
    private static T ComponentField<T>(CUE4Parse.UE4.Assets.Exports.UObject component, string name, T fallback = default!)
    {
        var seen = new HashSet<CUE4Parse.UE4.Assets.Exports.UObject>();
        for (var current = component; current is not null && seen.Add(current); current = current.Template?.Object?.Value)
            if (current.TryGet<T>(name, out var value)) return value;
        return fallback;
    }

    private static System.Numerics.Matrix4x4 ComponentPlacement(CUE4Parse.UE4.Assets.Exports.UObject component,
        HashSet<CUE4Parse.UE4.Assets.Exports.UObject> seen)
    {
        if (!seen.Add(component)) throw new InvalidDataException("Cyclic component attachment");
        var loc = ComponentField<CUE4Parse.UE4.Objects.Core.Math.FVector>(component, "RelativeLocation");
        var rot = ComponentField<CUE4Parse.UE4.Objects.Core.Math.FRotator>(component, "RelativeRotation").Quaternion();
        var scale = ComponentField(component, "RelativeScale3D", new CUE4Parse.UE4.Objects.Core.Math.FVector(1, 1, 1));
        var matrix = System.Numerics.Matrix4x4.CreateScale(scale.X, scale.Y, scale.Z)
            * System.Numerics.Matrix4x4.CreateFromQuaternion(new(rot.X, rot.Y, rot.Z, rot.W))
            * System.Numerics.Matrix4x4.CreateTranslation(loc.X, loc.Y, loc.Z);
        if (ComponentField<CUE4Parse.UE4.Assets.Exports.UObject?>(component, "AttachParent") is { } parent)
            matrix *= ComponentPlacement(parent, seen);
        return matrix;
    }

    /// <summary>The spawn actor linked to a cooked vehicle recall station, when present.</summary>
    public string? TryGetVehicleRecallSpawner(string actorObjectPath)
    {
        if (_disposed) return null;
        try
        {
            using (ProviderLock())
                return LoadActorKeepingLevel(actorObjectPath)?.GetOrDefault<CUE4Parse.UE4.Assets.Exports.UObject?>("LinkedSpawner")?.GetPathName();
        }
        catch { return null; }
    }

    private readonly Dictionary<string, System.Numerics.Matrix4x4?> _levelPlacements = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Like <see cref="TryGetActorTransform"/>, but in world space. An actor in a streamed area (for
    /// example <c>Facility_Office1</c>) is stored relative to that sub-level, and the sub-level is moved
    /// and turned into place by its streaming entry's <c>LevelTransform</c> in the map that streams it
    /// (<c>Facility</c>). Saved positions of player-built objects are world positions, so this is the one
    /// to compare them with. An actor of the outermost map is returned unchanged.
    /// </summary>
    public ActorTransform? TryGetActorWorldTransform(string? actorObjectPath)
    {
        if (TryGetActorTransform(actorObjectPath) is not { } local) return null;
        var mapName = MapNameOf(actorObjectPath!);
        if (mapName is null || PlacementOf(mapName) is not { } placement) return local;

        var localMatrix = System.Numerics.Matrix4x4.CreateFromQuaternion(
                              new System.Numerics.Quaternion((float)local.QuatX, (float)local.QuatY, (float)local.QuatZ, (float)local.QuatW))
                          * System.Numerics.Matrix4x4.CreateTranslation((float)local.X, (float)local.Y, (float)local.Z);
        var world = localMatrix * placement;
        System.Numerics.Matrix4x4.Decompose(world, out _, out var rotation, out var translation);
        return new ActorTransform(translation.X, translation.Y, translation.Z, rotation.X, rotation.Y, rotation.Z, rotation.W);
    }

    /// <summary>
    /// A position read from a sub-level (<paramref name="mapName"/>'s own coordinates, such as the root
    /// location the door resolver reports) placed into the world, the way
    /// <see cref="TryGetActorWorldTransform"/> places a whole actor. Returned unchanged for an outermost
    /// map or when no map streams it.
    /// </summary>
    public (double X, double Y, double Z) PlaceInWorld(string? mapName, double x, double y, double z)
    {
        if (string.IsNullOrEmpty(mapName) || PlacementOf(mapName) is not { } placement) return (x, y, z);
        var world = System.Numerics.Vector3.Transform(new System.Numerics.Vector3((float)x, (float)y, (float)z), placement);
        return (world.X, world.Y, world.Z);
    }

    /// <summary>"/Game/Maps/Facility_Office1.Facility_Office1:PersistentLevel.X" gives "Facility_Office1".</summary>
    private static string? MapNameOf(string actorObjectPath)
    {
        var colon = actorObjectPath.IndexOf(':', StringComparison.Ordinal);
        var package = colon > 0 ? actorObjectPath[..colon] : actorObjectPath;
        var dot = package.LastIndexOf('.');
        var name = dot >= 0 ? package[(dot + 1)..] : package[(package.LastIndexOf('/') + 1)..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Where a streamed map sits in the world: the <c>LevelTransform</c> of its entry in the outermost map
    /// its name nests under (<c>Facility_Dam_Central</c> is streamed by <c>Facility</c>). Null for the
    /// outermost map itself or when no entry names it. Found by name, so no map list is kept.
    /// </summary>
    private System.Numerics.Matrix4x4? PlacementOf(string mapName)
    {
        lock (_levelPlacements)
        {
            if (_levelPlacements.TryGetValue(mapName, out var cached)) return cached;
        }
        System.Numerics.Matrix4x4? result = null;
        try
        {
            // Candidates: the outermost maps the name nests under (Facility_Dam_Central in Facility),
            // then every single-word world map, for streamed maps whose name does not nest (the
            // portal worlds such as V_Alps, which Facility streams in far from its origin).
            var parts = mapName.Split('_');
            var candidates = new List<string>();
            for (var k = 1; k < parts.Length; k++)
            {
                var root = string.Join('_', parts[..k]);
                var rootPath = _provider.Files.Keys.FirstOrDefault(p =>
                    p.EndsWith("/" + root + ".umap", StringComparison.OrdinalIgnoreCase) && p.Contains("/Maps/", StringComparison.OrdinalIgnoreCase));
                if (rootPath is not null) candidates.Add(rootPath);
            }
            if (mapName.Contains('_', StringComparison.Ordinal))
            {
                candidates.AddRange(_provider.Files.Keys.Where(p =>
                    p.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) && p.Contains("/Maps/", StringComparison.OrdinalIgnoreCase)
                    && !Path.GetFileNameWithoutExtension(p).Contains('_', StringComparison.Ordinal)
                    && !Path.GetFileNameWithoutExtension(p).Equals(mapName, StringComparison.OrdinalIgnoreCase)
                    && !candidates.Contains(p, StringComparer.OrdinalIgnoreCase)));
            }
            foreach (var rootPath in candidates)
            {
                if (result is not null) break;
                using (ProviderLock())
                {
                    if (!_provider.TryLoadPackage(rootPath, out var package)) continue;
                    var world = GameMaps.WorldOf(package);
                    foreach (var index in world?.StreamingLevels ?? [])
                    {
                        var streaming = index.Load();
                        var asset = streaming?.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FSoftObjectPath>("WorldAsset").AssetPathName.Text;
                        if (asset is null || !asset.EndsWith("." + mapName, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!streaming!.TryGetValue(out CUE4Parse.UE4.Objects.Core.Math.FTransform t, "LevelTransform")) break;
                        var s = t.Scale3D;
                        var scale = s.X == 0 && s.Y == 0 && s.Z == 0 ? System.Numerics.Vector3.One : new System.Numerics.Vector3(s.X, s.Y, s.Z);
                        result = System.Numerics.Matrix4x4.CreateScale(scale)
                                 * System.Numerics.Matrix4x4.CreateFromQuaternion(new System.Numerics.Quaternion(t.Rotation.X, t.Rotation.Y, t.Rotation.Z, t.Rotation.W))
                                 * System.Numerics.Matrix4x4.CreateTranslation(t.Translation.X, t.Translation.Y, t.Translation.Z);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("Assets", $"Could not place level {mapName}: {ex.Message}");
        }
        lock (_levelPlacements)
        {
            _levelPlacements[mapName] = result;
        }
        return result;
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
        _recentPackagesTimer?.Dispose();
        if (_actorPositionsSaveTimer is not null)
        {
            _actorPositionsSaveTimer.Dispose();
            SaveActorPositions();
        }
        _provider.Dispose();
    }
}
