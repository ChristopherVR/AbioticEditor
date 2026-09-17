using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Research dump for the live ELEVATORS and BUTTONS areas: the full blueprint exports (script
/// bytecode included) of the elevator and button parent classes, so the live property and
/// function names the agent touches come from the game rather than from the save's leaf names.
/// Set <c>ELEVATOR_BUTTON_PROBE_OUT</c> to a directory to run it.
/// </summary>
public sealed class ElevatorButtonProbe
{
    [Fact]
    public void Dump_elevator_and_button_classes()
    {
        var output = Environment.GetEnvironmentVariable("ELEVATOR_BUTTON_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var paks = AfInstallLocator.FindPaksDirectory();
        Assert.NotNull(paks);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new CUE4Parse.MappingsProvider.Usmap.FileUsmapTypeMappingsProvider(GameAssetProvider.FindConventionalMappings()!);
        provider.ReadScriptData = true;
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));
        Directory.CreateDirectory(output);

        var elevatorAssets = provider.Files.Keys
            .Where(p => p.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                && p.Contains("Elevator", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        File.WriteAllLines(Path.Combine(output, "elevator-assets.txt"), elevatorAssets);

        string[] names = ["Elevator_ParentBP", "Elevator_Office_BP", "Button_Generic", "Button_Keypad", "Button_LightSwitch"];
        foreach (var path in provider.Files.Keys.Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                     && (names.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                         || (Path.GetFileNameWithoutExtension(path).StartsWith("E_", StringComparison.OrdinalIgnoreCase)
                             && path.Contains("Elevator", StringComparison.OrdinalIgnoreCase)))))
        {
            try
            {
                var exports = provider.LoadPackage(path).GetExports().ToArray();
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".error.txt"), ex.ToString());
            }
        }
    }
}
