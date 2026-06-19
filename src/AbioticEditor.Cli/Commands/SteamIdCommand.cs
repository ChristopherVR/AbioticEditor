using System.CommandLine;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>steamid &lt;player.sav&gt; &lt;newid&gt;</c> - re-homes a player save to another
/// owner: renames the file AND rewrites the internal SaveIdentifier together
/// (Core <see cref="PlayerSaveIdentity.ChangeSteamId"/>; .bak kept). The id is a
/// SteamID64 on Steam, or any safe token for a non-Steam (Game Pass / Epic) save.
/// </summary>
internal static class SteamIdCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("player")
        {
            Description = "Path to the Player_<id>.sav file to re-home.",
        };
        var idArg = new Argument<string>("newid")
        {
            Description = "The new owner id (a 17-digit SteamID64, or any safe token for non-Steam saves).",
        };

        var cmd = new Command("steamid", "Change which account a player save belongs to.");
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
        var id = newId?.Trim() ?? string.Empty;
        if (!PlayerIdentifier.IsSafeFileToken(id))
        {
            throw new CliUserErrorException(
                $"'{newId}' is not a valid player id (use letters, digits, '-', '_' or '.').");
        }

        // Core throws IOException when Player_<id>.sav already exists -> exit 1 via Cli.Run.
        var newPath = PlayerSaveIdentity.ChangeSteamId(path, id);
        Cli.Info(quiet, $"Re-homed {Path.GetFileName(path)} -> {Path.GetFileName(newPath)} "
            + "(SaveIdentifier rewritten, .bak kept). Note: bed claims in world saves still reference the old id.");
        return Cli.Ok;
    }
}
