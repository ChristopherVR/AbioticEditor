using System.CommandLine;
using System.Reflection;
using AbioticEditor.Core.SaveClasses;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>version</c> - tool version plus the ABF save versions this build was tested
/// against (<see cref="SaveCompatibility"/>). Newer saves still load; unmodeled fields
/// survive plain re-saves but may be lost on edits, so the editor warns about them.
/// </summary>
internal static class VersionCommand
{
    public static Command Build()
    {
        var cmd = new Command("version", "Show the tool version and supported save versions.");
        cmd.SetAction(_ => Cli.Run(Execute));
        return cmd;
    }

    private static int Execute()
    {
        var assembly = typeof(VersionCommand).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        Console.WriteLine($"abioticeditor {version}");
        Console.WriteLine("Supported ABF save versions (newer saves load with a warning):");
        Console.WriteLine($"  Character saves (Abiotic_CharacterSave_C): v{SaveCompatibility.KnownGoodCharacterVersion}");
        Console.WriteLine($"  World/metadata saves (Abiotic_WorldSave_C / Abiotic_WorldMetadataSave_C): v{SaveCompatibility.KnownGoodWorldVersion}");
        return Cli.Ok;
    }
}
