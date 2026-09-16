using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

public sealed class LiveAppearancePersistenceProbe
{
    [Fact]
    public void Dump_appearance_persistence()
    {
        var output = Environment.GetEnvironmentVariable("LIVE_APPEARANCE_PROBE_OUT");
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
            && (path.EndsWith("/Abiotic_PlayerCharacter.uasset", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/Abiotic_PlayerController.uasset", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/Abiotic_GameInstance.uasset", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/Abiotic_CustomizationSave.uasset", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/CharacterCustomization_BP.uasset", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Customization", StringComparison.OrdinalIgnoreCase) && path.Contains("Widget", StringComparison.OrdinalIgnoreCase))).ToArray();
        Assert.NotEmpty(paths);
        foreach (var path in paths)
        {
            var exports = provider.LoadPackage(path).GetExports().ToArray();
            File.WriteAllText(Path.Combine(output, Path.GetFileNameWithoutExtension(path) + ".json"), JsonConvert.SerializeObject(exports, Formatting.Indented));
        }
    }
}
