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

    /// <summary>
    /// Mounts the installed game's paks, serialized against every other mount in the
    /// process. Returns null when there is no readable install. Callers own the result and
    /// must dispose it (or hold it for the session, as the long-lived services do).
    /// </summary>
    public static GameAssetProvider? CreateProvider(string? culture = null)
    {
        lock (Sync)
        {
            return culture is null
                ? GameAssetProvider.CreateForLocalInstall()
                : GameAssetProvider.CreateForLocalInstall(culture: culture);
        }
    }
}
