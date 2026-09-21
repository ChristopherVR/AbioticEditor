using AbioticEditor.Core.Assets;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Process-wide gate for mounting the game paks. Several vocabularies load lazily the
/// first time a tab needs them; when two loads raced on first use, CUE4Parse's global
/// initialization could fail one of them and the empty result was then cached for the
/// whole session ("game data unavailable" on a machine with the game installed).
/// Serializing the mounts (and retrying a failed load on the next request instead of
/// caching the failure) makes first-tab-visit loads deterministic.
///
/// Taking the lock around a load is not enough on its own: the race is in the MOUNT, so
/// every provider in the app has to be created through <see cref="CreateProvider"/>. Two
/// services holding their own locks still collide. This is why the recipe list could come
/// back empty on the General tab (and only that tab) while item icons were extracting -
/// the icon service built its provider outside the gate.
/// </summary>
internal static class GameDataGate
{
    public static readonly object Sync = new();
    private static readonly Dictionary<string, GameAssetProvider> Providers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<GameAssetProvider> RetiredProviders = [];

    static GameDataGate() => AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeProviders();

    /// <summary>
    /// Mounts the installed game's paks, serialized against every other mount in the
    /// process. Returns null when there is no readable install. The gate owns shared
    /// providers; callers must not dispose them.
    /// </summary>
    public static GameAssetProvider? CreateProvider(string? culture = null)
    {
        lock (Sync)
        {
            // Null and English both use the same baked-in game text.
            culture = string.IsNullOrWhiteSpace(culture) ? "en" : culture;
            var paks = AfInstallLocator.FindPaksDirectory();
            if (paks is null) return null;
            var mappings = GameAssetProvider.FindConventionalMappings() ?? string.Empty;
            var disabledModsStamp = File.Exists(ModLoadStore.DisabledModsPath)
                ? File.GetLastWriteTimeUtc(ModLoadStore.DisabledModsPath).Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
            var key = string.Join("|", paks, mappings, culture ?? string.Empty,
                ModLoadStore.ModsEnabled, disabledModsStamp);
            if (!Providers.TryGetValue(key, out var provider))
            {
                provider = GameAssetProvider.CreateForLocalInstall(culture: culture);
                if (provider is not null) Providers[key] = provider;
            }

            // Providers are process-scoped. Callers must not dispose this result: the
            // singleton vocabularies share it for the lifetime of the desktop host.
            return provider;
        }
    }

    /// <summary>Drops cached mounts after game-data settings change.</summary>
    public static void Invalidate()
    {
        lock (Sync)
        {
            // Existing consumers may still be reading a provider. Retain retired mounts
            // until process exit instead of disposing underneath an in-flight catalog read.
            RetiredProviders.AddRange(Providers.Values);
            Providers.Clear();
        }
    }

    private static void DisposeProviders()
    {
        lock (Sync)
        {
            foreach (var provider in Providers.Values.Concat(RetiredProviders).Distinct())
            {
                try { provider.Dispose(); } catch { }
            }
            Providers.Clear();
            RetiredProviders.Clear();
        }
    }
}
