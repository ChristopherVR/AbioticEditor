using System.Text.RegularExpressions;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.Steam;

/// <summary>
/// Resolves owner ids to display names so player saves can be listed as "Tribbes" instead
/// of a raw id. Two offline sources: the Steam client's <c>config\loginusers.vdf</c>
/// (Steam accounts that logged in on this machine) and bed-claim strings in the world save
/// (every co-op player who claimed a bed, with their in-game name). Ids are kept as opaque
/// strings; the vdf source only ever yields numeric SteamID64 keys, but a non-Steam owner
/// resolves through its bed-claim name (or falls back to the raw id).
/// </summary>
public static class SteamPersonaIndex
{
    /// <summary>Persona names of accounts that have logged into Steam on this machine, keyed by
    /// SteamID64 (as a string).</summary>
    public static IReadOnlyDictionary<string, string> LoadMachineAccounts()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var steam = Assets.AfInstallLocator.FindSteamPath();
        if (steam is null) return result;

        var vdf = Path.Combine(steam, "config", "loginusers.vdf");
        if (!File.Exists(vdf)) return result;

        try
        {
            // VDF shape: "765611..." { ... "PersonaName" "Tribbes" ... }
            var text = File.ReadAllText(vdf);
            foreach (Match block in Regex.Matches(
                text, "\"(7656119\\d{10})\"\\s*\\{(.*?)\\}", RegexOptions.Singleline))
            {
                var persona = Regex.Match(block.Groups[2].Value, "\"PersonaName\"\\s+\"([^\"]*)\"");
                if (persona.Success && persona.Groups[1].Value.Length > 0)
                {
                    result[block.Groups[1].Value] = persona.Groups[1].Value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Personas", $"Could not read loginusers.vdf: {ex.Message}");
        }
        return result;
    }

    /// <summary>In-game names from bed-claim strings (<c>id}|!|{name</c>), keyed by owner id.</summary>
    public static IReadOnlyDictionary<string, string> FromDeployables(IEnumerable<WorldDeployable> deployables)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var d in deployables)
        {
            if (d.OwnerId is { Length: > 0 } id && d.OwnerName is { Length: > 0 } name)
            {
                result[id] = name;
            }
        }
        return result;
    }

    /// <summary>The owner id encoded in a <c>Player_&lt;id&gt;.sav</c> file name, or null.</summary>
    public static string? IdFromPlayerPath(string path)
        => PlayerIdentifier.TryParseFromPlayerFileName(path, out var id) ? id : null;

    /// <summary>
    /// Best display name for an owner id: a bed-claim name (what teammates see) wins, then a
    /// Steam persona for a known SteamID64, then the raw id. A non-Steam id with no claim name
    /// is returned verbatim with a "(non-Steam)" hint when <paramref name="hintNonSteam"/> is set.
    /// </summary>
    public static string ResolveDisplayName(
        string id,
        IReadOnlyDictionary<string, string>? machineAccounts = null,
        IReadOnlyDictionary<string, string>? claimNames = null,
        bool hintNonSteam = false)
    {
        if (claimNames is not null && claimNames.TryGetValue(id, out var claimName) && claimName.Length > 0)
        {
            return claimName;
        }
        if (PlayerIdentifier.IsSteamId(id))
        {
            var accounts = machineAccounts ?? LoadMachineAccounts();
            if (accounts.TryGetValue(id, out var persona) && persona.Length > 0)
            {
                return persona;
            }
            return id;
        }
        return hintNonSteam ? $"{id} (non-Steam)" : id;
    }
}
