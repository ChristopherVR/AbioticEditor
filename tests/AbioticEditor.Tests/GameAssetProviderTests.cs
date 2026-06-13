using System.IO;
using AbioticEditor.Core.Assets;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class GameAssetProviderTests
{
    private readonly ITestOutputHelper _output;

    public GameAssetProviderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void ExtractFont_ProducesUsableTtf()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;

        var path = provider.ExtractFontAsTtf("AbioticFactor/Content/Blueprints/Widgets/Fonts/digital-7");
        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        _output.WriteLine($"Font extracted to {path} ({new FileInfo(path!).Length:N0} bytes)");

        var head = new byte[4];
        using var fs = File.OpenRead(path!);
        Assert.Equal(4, fs.Read(head, 0, 4));
        var asString = System.Text.Encoding.ASCII.GetString(head);
        var validSignatures = new[] { "OTTO", "true", "ttcf" };
        var isClassicTtf = head[0] == 0x00 && head[1] == 0x01 && head[2] == 0x00 && head[3] == 0x00;
        Assert.True(isClassicTtf || validSignatures.Contains(asString),
            $"unexpected font signature: {BitConverter.ToString(head)} ({asString})");
    }

    [Fact]
    public void ExtractTexture_RequiresUsmapMappings_WhenAbsent()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;
        if (provider.HasMappings)
        {
            _output.WriteLine("Mappings present — skipping the no-mappings assertion.");
            return;
        }

        var ex = Assert.Throws<GameAssetProvider.MappingsRequiredException>(() =>
            provider.ExtractTextureAsPng("AbioticFactor/Content/Textures/GUI/Inventory/T_ABF_Logo_1024"));

        _output.WriteLine($"Got expected exception: {ex.Message}");
    }

    [Fact]
    public void ExtractLogo_ProducesValidPng_WhenMappingsAvailable()
    {
        using var provider = GameAssetProvider.CreateForLocalInstall();
        if (provider is null) return;
        if (!provider.HasMappings)
        {
            _output.WriteLine(
                $"Skipping — no .usmap mapping file. Place one at " +
                $"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "mappings", "Mappings.usmap")} " +
                $"to enable texture extraction. See README.");
            return;
        }

        string?[] candidates =
        {
            "AbioticFactor/Content/Textures/GUI/Inventory/T_ABF_Logo_1024",
            "AbioticFactor/Content/Textures/GUI/Logos/ABF-Full-Color-1024w",
        };

        string? extracted = null;
        foreach (var p in candidates)
        {
            extracted = provider.ExtractTextureAsPng(p!);
            if (extracted is not null) break;
        }

        Assert.NotNull(extracted);
        var header = new byte[8];
        using (var fs = File.OpenRead(extracted!))
        {
            Assert.Equal(8, fs.Read(header, 0, 8));
        }
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, header);
        _output.WriteLine($"Logo PNG: {extracted} ({new FileInfo(extracted!).Length:N0} bytes)");
    }
}
