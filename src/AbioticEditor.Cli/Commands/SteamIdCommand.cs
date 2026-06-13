using System.CommandLine;
using System.Globalization;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>steamid &lt;player.sav&gt; &lt;newid64&gt;</c> - re-homes a player save to another
/// Steam account: renames the file AND rewrites the internal SaveIdentifier together
/// (Core <see cref="PlayerSaveIdentity.ChangeSteamId"/>; .bak kept).
/// </summary>
internal static class SteamIdCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("player")
        {
            Description = "Path to the Player_<steamid64>.sav file to re-home.",
        };
        var idArg = new Argument<string>("newid64")
        {
            Description = "The new owner's SteamID64 (17-digit number).",
        };

        var cmd = new Command("steamid", "Change which Steam account a player save belongs to.");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(idArg);
        cmd.SetAction(parseResult => Cli.Run(() => Execute(
            parseResult.GetValue(saveArg),
            parseResult.GetValue(idArg),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Execute(string? save, string? newId, bool quiet)
    {
        var path = Cli.RequireFile(save, "player save");
        if (!ulong.TryParse(newId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
        {
            throw new CliUserErrorException($"'{newId}' is not a valid SteamID64.");
        }

        // Core throws IOException when Player_<id>.sav already exists -> exit 1 via Cli.Run.
        var newPath = PlayerSaveIdentity.ChangeSteamId(path, id);
        Cli.Info(quiet, $"Re-homed {Path.GetFileName(path)} -> {Path.GetFileName(newPath)} "
            + "(SaveIdentifier rewritten, .bak kept). Note: bed claims in world saves still reference the old id.");
        return Cli.Ok;
    }
}
