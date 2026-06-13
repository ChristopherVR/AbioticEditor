using System.Text.RegularExpressions;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Core.Steam;

/// <summary>
/// Resolves steamid64 values to display names so player saves can be listed as
/// "Tribbes" instead of a 17-digit number. Two offline sources:
/// the Steam client's <c>config\loginusers.vdf</c> (accounts that logged in on this
/// machine) and bed-claim strings in the world save (every co-op player who claimed
/// a bed, with their in-game name).
/// </summary>
public static class SteamPersonaIndex
{
    /// <summary>Persona names of accounts that have logged into Steam on this machine.</summary>
    public static IReadOnlyDictionary<ulong, string> LoadMachineAccounts()
    {
        var result = new Dictionary<ulong, string>();
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
                if (!ulong.TryParse(block.Groups[1].Value, out var id)) continue;
                var persona = Regex.Match(block.Groups[2].Value, "\"PersonaName\"\\s+\"([^\"]*)\"");
                if (persona.Success && persona.Groups[1].Value.Length > 0)
                {
                    result[id] = persona.Groups[1].Value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.EditorLog.Warn("Personas", $"Could not read loginusers.vdf: {ex.Message}");
        }
        return result;
    }

    /// <summary>In-game names from bed-claim strings (<c>id}|!|{name</c>).</summary>
    public static IReadOnlyDictionary<ulong, string> FromDeployables(IEnumerable<WorldDeployable> deployables)
    {
        var result = new Dictionary<ulong, string>();
        foreach (var d in deployables)
        {
            if (d.OwnerSteamId is { } id && d.OwnerName is { Length: > 0 } name)
            {
                result[id] = name;
            }
        }
        return result;
    }

    /// <summary>The steamid64 encoded in a <c>Player_&lt;id64&gt;.sav</c> file name, or null.</summary>
    public static ulong? SteamIdFromPlayerPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        const string prefix = "Player_";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && ulong.TryParse(name[prefix.Length..], out var id)
            ? id
            : null;
    }
}
