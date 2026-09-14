using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

public sealed class LiveParityClassProbe
{
    [Fact]
    public void Dump_player_initialization_and_customization()
    {
        var output = Environment.GetEnvironmentVariable("LIVE_PARITY_PROBE_OUT");
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
        var paths = provider.Files.Keys.Where(path => path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
            && (path.EndsWith("/Abiotic_CharacterProgressionComponent.uasset", StringComparison.OrdinalIgnoreCase)
                || path.Contains("HumanCustomizationComp", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/Abiotic_PlayerState.uasset", StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var exports = provider.LoadPackage(path).GetExports().ToArray();
            File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
        }
    }
}
