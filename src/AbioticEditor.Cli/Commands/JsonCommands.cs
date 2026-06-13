using System.CommandLine;
using AbioticEditor.Core;
using UeSaveGame;

using AbioticEditor.Core.Saves;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>export-json</c> / <c>import-json</c> - the same byte-perfect JSON round trip the
/// app's raw-JSON tab uses (<see cref="SaveJsonBridge"/> over UeSaveGame.Json).
/// </summary>
internal static class JsonCommands
{
    public static Command BuildExport(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save")
        {
            Description = "Path to the .sav file to export.",
        };
        var outOpt = new Option<string?>("--output", "-o")
        {
            Description = "Output .json path (default: the save path with a .json extension).",
        };

        var cmd = new Command("export-json", "Dump a save's full contents to JSON (lossless; world saves can be 100+ MB).");
        cmd.Arguments.Add(saveArg);
        cmd.Options.Add(outOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Export(
            parseResult.GetValue(saveArg),
            parseResult.GetValue(outOpt),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    public static Command BuildImport(Option<bool> quiet)
    {
        var saveArg = new Argument<string>("save")
        {
            Description = "The .sav file to overwrite (a .bak of the previous content is kept).",
        };
        var jsonArg = new Argument<string>("json")
        {
            Description = "The edited .json file to convert back into save bytes.",
        };

        var cmd = new Command("import-json", "Rebuild a .sav from an exported JSON file (the save is untouched if the JSON fails to parse).");
        cmd.Arguments.Add(saveArg);
        cmd.Arguments.Add(jsonArg);
        cmd.SetAction(parseResult => Cli.Run(() => Import(
            parseResult.GetValue(saveArg),
            parseResult.GetValue(jsonArg),
            parseResult.GetValue(quiet))));
        return cmd;
    }

    private static int Export(string? save, string? output, bool quiet)
    {
        var savPath = Cli.RequireFile(save, "save file");
        var jsonPath = string.IsNullOrWhiteSpace(output)
            ? Path.ChangeExtension(savPath, ".json")
            : Path.GetFullPath(output);

        AbioticSaveClasses.EnsureLoaded();
        SaveGame loaded;
        using (var fs = File.OpenRead(savPath))
        {
            loaded = SaveGame.LoadFrom(fs);
        }

        SaveJsonBridge.ExportJsonToFile(loaded, jsonPath);
        Cli.Info(quiet, $"Exported {savPath} -> {jsonPath}");
        return Cli.Ok;
    }

    private static int Import(string? save, string? json, bool quiet)
    {
        var savPath = Cli.RequireFile(save, "save file");
        var jsonPath = Cli.RequireFile(json, "json file");

        SaveJsonBridge.ImportJsonFromFile(jsonPath, savPath);
        Cli.Info(quiet, $"Imported {jsonPath} -> {savPath} (previous content kept as {Path.GetFileName(savPath)}.bak)");
        return Cli.Ok;
    }
}
