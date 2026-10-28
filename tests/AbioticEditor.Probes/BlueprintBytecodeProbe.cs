using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for live editing: writes the compiled Blueprint bytecode of chosen game classes
/// (one JSON file per function), so a live call can follow exactly what the game's own Blueprint
/// does instead of guessing. Output-only; runs when BP_BYTECODE_OUT names an output folder and
/// BP_BYTECODE_CLASSES lists Blueprint asset names separated by ';' (for example
/// "BuffActor_Follower;Abiotic_PlayerState").
/// </summary>
public class BlueprintBytecodeProbe
{
    [Fact]
    public void Dump_BlueprintBytecode()
    {
        var outDir = Environment.GetEnvironmentVariable("BP_BYTECODE_OUT");
        var classes = Environment.GetEnvironmentVariable("BP_BYTECODE_CLASSES");
        if (string.IsNullOrWhiteSpace(outDir) || string.IsNullOrWhiteSpace(classes)) return;
        Directory.CreateDirectory(outDir);

        var paks = AfInstallLocator.FindPaksDirectory();
        var mappingsPath = GameAssetProvider.FindConventionalMappings();
        Assert.NotNull(paks);
        Assert.NotNull(mappingsPath);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath!);
        provider.ReadScriptData = true;
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        foreach (var name in classes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = provider.Files.Keys.FirstOrDefault(k => k.EndsWith("/" + name + ".uasset", StringComparison.OrdinalIgnoreCase));
            if (key is null) { File.WriteAllText(Path.Combine(outDir, name + ".missing.txt"), "not found"); continue; }
            var package = provider.LoadPackage(key[..^".uasset".Length]);
            foreach (var function in package.GetExports().OfType<UFunction>())
                File.WriteAllText(Path.Combine(outDir, $"{name}.{string.Concat(function.Name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'))}.json"),
                    JsonConvert.SerializeObject(function, Formatting.Indented));
        }
    }
}
