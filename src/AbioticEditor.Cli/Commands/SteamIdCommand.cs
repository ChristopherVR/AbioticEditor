using System.CommandLine;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>steamid &lt;player.sav&gt; &lt;newid&gt;</c> - re-homes a player save to another
/// owner: renames the file, rewrites the internal SaveIdentifier
/// (Core <see cref="PlayerSaveIdentity.ChangeSteamId"/>; .bak kept) and moves the bed claims in
/// the surrounding world folder across too (Core <see cref="WorldSteamIdPatcher"/>). The id is a
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

        // The old id has to be read before the rename, because the file name is where it lives.
        var hasOldId = PlayerIdentifier.TryParseFromPlayerFileName(path, out var oldId);

        // Core throws IOException when Player_<id>.sav already exists -> exit 1 via Cli.Run.
        var newPath = PlayerSaveIdentity.ChangeSteamId(path, id);

        // Beds and other claimables record their owner in the world saves that sit one level up
        // from PlayerData, so the character keeps what it claimed instead of arriving locked out.
        var claims = 0;
        var worldDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(newPath)));
        if (hasOldId && worldDir is not null)
        {
            claims = WorldSteamIdPatcher.PatchFolder(worldDir, oldId, id);
        }

        Cli.Info(quiet, $"Re-homed {Path.GetFileName(path)} -> {Path.GetFileName(newPath)} "
            + $"(SaveIdentifier rewritten, .bak kept). {claims} bed claim(s) moved to the new id.");
        return Cli.Ok;
    }
}
