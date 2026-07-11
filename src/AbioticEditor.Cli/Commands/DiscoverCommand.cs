using System.CommandLine;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>discover</c> - lists every Abiotic Factor world found on this machine: Steam and
/// Game Pass client saves, Steam Play / Proton prefixes (Linux, Steam Deck), and
/// dedicated-server installs. The paths it prints are what the other commands take.
/// </summary>
internal static class DiscoverCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit a JSON array instead of the human-readable table.",
        };

        var cmd = new Command(
            "discover",
            "Find every Abiotic Factor world on this machine (client, Proton, Game Pass, dedicated server).");
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Execute(
            parseResult.GetValue(jsonOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Execute(bool json, bool quiet)
    {
        var worlds = SaveDiscovery.DiscoverAll();

        if (json)
        {
            Cli.WriteJson(worlds.Select(w => new
            {
                world = w.WorldName,
                path = w.FolderPath,
                source = w.SourceLabel,
                platform = w.PlatformLabel,
                accountId = w.AccountId,
                gamePassContainer = w.GamePassContainer,
                saveFiles = w.SaveFileCount,
                lastPlayed = w.LastPlayed,
            }));
            return Cli.Ok;
        }

        if (worlds.Count == 0)
        {
            Cli.Info(quiet, "No worlds found on this machine.");
            return Cli.Ok;
        }

        Console.WriteLine($"{"PLATFORM",-9} {"SOURCE",-6} {"WORLD",-24} {"SAVES",5}  {"LAST PLAYED",-16}  PATH");
        foreach (var w in worlds)
        {
            var lastPlayed = w.LastPlayed == DateTime.MinValue ? "-" : w.LastPlayed.ToString("yyyy-MM-dd HH:mm");
            Console.WriteLine(
                $"{w.PlatformLabel,-9} {w.SourceLabel,-6} {w.WorldName,-24} {w.SaveFileCount,5}  {lastPlayed,-16}  {w.FolderPath}");
        }
        Cli.Info(quiet, $"{worlds.Count} world(s).");
        return Cli.Ok;
    }
}
