using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace AbioticEditor.Tests;

public sealed class PikemanNameProbe
{
    [Fact]
    public void Find_pikeman_and_exor_paths()
    {
        var out_ = Environment.GetEnvironmentVariable("PIKEMAN_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(out_)) return;
        var paks = AfInstallLocator.FindPaksDirectory();
        Assert.NotNull(paks);
#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x" + new string('0', 64)));
        var hits = provider.Files.Keys.Where(p => p.Contains("Pikeman", StringComparison.OrdinalIgnoreCase)
            || p.Contains("Exor", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p).ToArray();
        File.WriteAllLines(out_, hits);
    }
}
