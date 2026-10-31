namespace AbioticEditor.Core.Assets;

/// <summary>What a mod did to one piece of game data.</summary>
public enum ModChange
{
    /// <summary>The mod brought something the base game does not have (a new trader, item, recipe).</summary>
    Added,

    /// <summary>The mod rewrote something the base game already has (for example a trader's stock).</summary>
    Changed,
}

/// <summary>
/// Which loaded mod a piece of game data came from. A catalog entry with no origin is base game.
/// This is the editor's single answer to "is this a mod or not": anything the base game paks
/// define is base game, and anything a mounted mod archive adds or overrides carries an origin.
/// </summary>
/// <param name="ModKey">The editor's key for the mod (<c>mod.io/&lt;id&gt;</c>, or a legacy pak name).</param>
/// <param name="ModName">The mod's display name.</param>
/// <param name="ModIoId">The mod.io id, when the mod came from mod.io.</param>
/// <param name="Change">Whether the mod added this, or changed something the game already had.</param>
public sealed record ModOrigin(string ModKey, string ModName, long? ModIoId, ModChange Change);

/// <summary>One row a mod adds to, or changes in, one of the game's data tables.</summary>
/// <param name="Table">The target table's file name, e.g. <c>DT_NPC_Traders</c>.</param>
/// <param name="RowId">The row id as the game sees it (mod prefix included for additions).</param>
public sealed record ModRowContribution(string Table, string RowId, ModOrigin Origin);
