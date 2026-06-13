using System.CommandLine;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>scan &lt;folder&gt;</c> - lists every save under a folder with its kind and
/// ABF save version (header-only probe; the property bodies are never parsed).
/// </summary>
internal static class ScanCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var folderArg = new Argument<string>("folder")
        {
            Description = "Folder to scan recursively for *.sav files (e.g. a Worlds/<World> directory).",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit a JSON array instead of the human-readable table.",
        };

        var cmd = new Command("scan", "List the save files under a folder with kind and save version.");
        cmd.Arguments.Add(folderArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Execute(
            parseResult.GetValue(folderArg),
            parseResult.GetValue(jsonOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Execute(string? folder, bool json, bool quiet)
    {
        var dir = Cli.RequireDirectory(folder, "folder");
        var summaries = SaveFolderScanner.Scan(dir);

        var rows = summaries.Select(s => new
        {
            s.FullPath,
            File = s.DisplayName,
            Kind = s.KindLabel,
            s.SaveClass,
            AbfVersion = ProbeVersion(s),
            s.SizeBytes,
            Size = s.SizeText,
            Error = s.LoadError,
        }).ToList();

        if (json)
        {
            Cli.WriteJson(rows.Select(r => new
            {
                path = r.FullPath,
                file = r.File,
                kind = r.Kind,
                saveClass = r.SaveClass,
                abfVersion = r.AbfVersion,
                sizeBytes = r.SizeBytes,
                error = r.Error,
            }));
            return Cli.Ok;
        }

        if (rows.Count == 0)
        {
            Cli.Info(quiet, $"No .sav files under {dir}.");
            return Cli.Ok;
        }

        Console.WriteLine($"{"KIND",-7} {"VER",3}  {"SIZE",9}  FILE");
        foreach (var r in rows)
        {
            var version = r.AbfVersion is int v ? $"{v}" : "-";
            var suffix = r.Error is null ? string.Empty : $"  [{r.Error}]";
            Console.WriteLine($"{r.Kind,-7} {version,3}  {r.Size,9}  {r.File}{suffix}");
        }
        Cli.Info(quiet, $"{rows.Count} save(s); {rows.Count(r => r.Error is not null)} unreadable.");
        return Cli.Ok;
    }

    private static int? ProbeVersion(SaveFileSummary summary)
    {
        if (summary.LoadError is not null)
        {
            return null;
        }
        try
        {
            return SaveFolderScanner.ReadHeaderInfo(summary.FullPath).AbfVersion;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            return null;
        }
    }
}
