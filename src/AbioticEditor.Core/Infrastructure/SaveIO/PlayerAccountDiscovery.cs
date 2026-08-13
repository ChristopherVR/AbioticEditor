using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Steam;

namespace AbioticEditor.Core.Saves;

/// <summary>One account this machine has some trace of, offered as a shortcut wherever the
/// editor needs a player account.</summary>
/// <param name="AccountId">The id exactly as it names a save folder or a player save, opaque.</param>
/// <param name="Platform">Which storefront the id belongs to, so the two can be told apart.</param>
public sealed record DiscoveredAccount(string AccountId, SavePlatform Platform)
{
    /// <summary>A human name for the account (a Steam persona), or null when only the id is known.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// Collects the player accounts this machine knows about, from every place an id can turn up:
/// the account folder of each discovered world, the Xbox account folders holding Game Pass saves,
/// and the Steam accounts that have signed in on this machine (which carry a persona name, and
/// count even when they have never played Abiotic Factor).
/// </summary>
/// <remarks>
/// This exists so a screen that needs an account id can offer the ones that are actually here
/// instead of asking the player to find a 17-digit number themselves. It is only ever a shortcut:
/// the list is not a permitted set, and an id nobody here has heard of is perfectly normal (moving
/// a world to an account that has not played yet is the usual reason to want one at all).
/// </remarks>
public static class PlayerAccountDiscovery
{
    /// <summary>
    /// Scans this machine. Never throws; each underlying scan skips what it cannot read.
    /// Empty on a machine with no game and no Steam client, which is a normal answer.
    /// </summary>
    public static IReadOnlyList<DiscoveredAccount> DiscoverAll()
        => Gather(
            SaveDiscovery.DiscoverAll(),
            GamePassDiscovery.DiscoverAll(),
            SteamPersonaIndex.LoadMachineAccounts());

    /// <summary>
    /// Merges the three sources into one ordered, de-duplicated list. Exposed separately from
    /// <see cref="DiscoverAll"/> so the merge rules can be tested without a game install.
    /// </summary>
    public static IReadOnlyList<DiscoveredAccount> Gather(
        IEnumerable<DiscoveredWorld>? worlds,
        IEnumerable<DiscoveredGamePassSave>? gamePassSaves,
        IReadOnlyDictionary<string, string>? steamPersonas)
    {
        // Ids are compared without case because an Xbox account folder is hexadecimal and the
        // same account can be written either way by different tools.
        var found = new Dictionary<string, DiscoveredAccount>(StringComparer.OrdinalIgnoreCase);

        foreach (var world in worlds ?? Array.Empty<DiscoveredWorld>())
        {
            // Server worlds have no owning account folder, so they contribute nothing here.
            Add(found, world.AccountId, world.Platform, displayName: null);
        }

        foreach (var save in gamePassSaves ?? Array.Empty<DiscoveredGamePassSave>())
        {
            Add(found, save.AccountId, SavePlatform.GamePass, displayName: null);
        }

        // Last, so a persona name lands on an account already found through a world.
        foreach (var (id, persona) in steamPersonas ?? new Dictionary<string, string>())
        {
            Add(found, id, SavePlatform.Steam, persona);
        }

        return found.Values
            .OrderBy(account => PlatformRank(account.Platform))
            .ThenBy(account => account.DisplayName ?? account.AccountId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Records an account, or fills in what an earlier source did not know. The same account
    /// turns up from several scans (a Steam world folder and the Steam client both name it), and
    /// each scan knows a different half of the answer, so later passes may only add: a named
    /// platform replaces an unknown one and a persona replaces a bare id, never the other way.
    /// </summary>
    private static void Add(
        Dictionary<string, DiscoveredAccount> found, string? id, SavePlatform platform, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var accountId = id.Trim();

        // Discovery infers the same thing from a folder name; an id that arrived from somewhere
        // with no platform attached is still plainly a Steam one when it is shaped like a SteamID64.
        if (platform == SavePlatform.Unknown && PlayerIdentifier.IsSteamId(accountId))
        {
            platform = SavePlatform.Steam;
        }
        var name = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        if (!found.TryGetValue(accountId, out var existing))
        {
            found[accountId] = new DiscoveredAccount(accountId, platform) { DisplayName = name };
            return;
        }

        found[accountId] = existing with
        {
            Platform = existing.Platform == SavePlatform.Unknown ? platform : existing.Platform,
            DisplayName = existing.DisplayName ?? name,
        };
    }

    // Steam first: it is the platform whose ids a player is most likely to be typing, and the
    // only one the editor can say anything sensible about.
    private static int PlatformRank(SavePlatform platform) => platform switch
    {
        SavePlatform.Steam => 0,
        SavePlatform.GamePass => 1,
        _ => 2,
    };
}
