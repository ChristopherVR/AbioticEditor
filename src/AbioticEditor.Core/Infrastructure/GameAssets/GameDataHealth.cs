using AbioticEditor.Core.Items;

namespace AbioticEditor.Core.Assets;

/// <summary>Whether the installed game's data tables can actually be read with the mappings in use.</summary>
public enum GameDataHealthStatus
{
    /// <summary>The core item table read back with a plausible number of rows.</summary>
    Healthy = 0,

    /// <summary>
    /// The game and a mappings file are present but the item table came back empty or nearly so.
    /// That is what a game update leaves behind when the mappings describe the previous build.
    /// </summary>
    MappingsOutdated,

    /// <summary>The check itself failed (for example a file was locked), so nothing can be said either way.</summary>
    Unreadable,
}

/// <summary>
/// A self-test for the game-data pipeline. The paks mount and the mappings load even when the
/// mappings no longer match the game: the tables then parse to nothing and every catalog is empty
/// without an error. This reads the largest core table and checks it holds a believable number
/// of rows, so a stale mappings file is noticed instead of silently producing an empty editor.
/// </summary>
public static class GameDataHealth
{
    /// <summary>
    /// Fewer item rows than this means the table did not parse. The base game has well over a
    /// thousand; a deliberately low floor keeps a partly modded or trimmed install healthy.
    /// </summary>
    public const int MinimumPlausibleItemRows = 200;

    /// <summary>Pure classification of one probe result, separated out so it can be tested without a game.</summary>
    public static GameDataHealthStatus Classify(int? itemRows)
        => itemRows is not { } rows
            ? GameDataHealthStatus.Unreadable
            : rows >= MinimumPlausibleItemRows ? GameDataHealthStatus.Healthy : GameDataHealthStatus.MappingsOutdated;

    /// <summary>
    /// Probes <paramref name="provider"/>. A provider without mappings is reported as
    /// <see cref="GameDataHealthStatus.Unreadable"/> because that case already has its own message.
    /// The item catalog is cached per provider, so the probe costs nothing extra later.
    /// </summary>
    public static GameDataHealthStatus Check(GameAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (!provider.HasMappings) return GameDataHealthStatus.Unreadable;
        try
        {
            return Classify(ItemCatalog.LoadFrom(provider).Count);
        }
        catch (IOException ex)
        {
            Diagnostics.EditorLog.Warn("GameDataHealth", "The game files could not be read to check the game data.", ex);
            return GameDataHealthStatus.Unreadable;
        }
        catch (Exception ex)
        {
            Diagnostics.EditorLog.Warn("GameDataHealth", "The item table could not be parsed with the current mappings.", ex);
            return GameDataHealthStatus.MappingsOutdated;
        }
    }
}
