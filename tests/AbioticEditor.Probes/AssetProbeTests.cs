using System.IO;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

public class AssetProbeTests
{
    private readonly ITestOutputHelper _output;

    public AssetProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Probe_DetectInstallAndEncryption()
    {
        var paks = AfInstallLocator.FindPaksDirectory();
        _output.WriteLine($"Paks directory: {paks ?? "<not found>"}");
        if (paks is null)
        {
            // No AF on this machine - nothing to probe.
            return;
        }

        // Try every plausible UE5 version until one initializes without throwing.
        // Iostore format details shifted across UE5.3 / 5.4 / 5.5.
        EGame[] candidates = { EGame.GAME_UE5_4, EGame.GAME_UE5_3, EGame.GAME_UE5_5, EGame.GAME_UE5_2 };

        foreach (var game in candidates)
        {
            _output.WriteLine($"--- Trying {game} ---");
            DefaultFileProvider? provider = null;
            try
            {
#pragma warning disable CS0618 // obsolete ctor - kept for simplicity; new one needs different arg order
                provider = new DefaultFileProvider(
                    paks,
                    SearchOption.TopDirectoryOnly,
                    isCaseInsensitive: true,
                    new VersionContainer(game));
#pragma warning restore CS0618
                provider.Initialize();

                _output.WriteLine($"  Initialize OK. Required AES keys: {provider.RequiredKeys.Count}");
                foreach (var guid in provider.RequiredKeys)
                {
                    _output.WriteLine($"    needs key for {guid}");
                }

                // For unencrypted archives the GUID is FGuid.Empty; submitting a dummy
                // key against the empty guid triggers the mount step.
                var dummy = new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000");
                var mounted = provider.SubmitKey(new FGuid(), dummy);
                _output.WriteLine($"  SubmitKey(empty) mounted: {mounted} archive(s)");
                _output.WriteLine($"  Files visible after mount: {provider.Files.Count}");

                if (provider.Files.Count > 0)
                {
                    var sample = provider.Files.Keys.Take(25).ToList();
                    _output.WriteLine("  First 25 asset paths:");
                    foreach (var path in sample)
                    {
                        _output.WriteLine($"    {path}");
                    }

                    var distinctExts = provider.Files.Keys
                        .Select(Path.GetExtension)
                        .Where(e => !string.IsNullOrEmpty(e))
                        .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count())
                        .Take(15)
                        .ToList();
                    _output.WriteLine("  Top extensions:");
                    foreach (var g in distinctExts)
                    {
                        _output.WriteLine($"    {g.Key}  ×{g.Count()}");
                    }
                }

                return;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  Failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                provider?.Dispose();
            }
        }

        Assert.Fail("None of the candidate UE5 versions initialized successfully.");
    }
}
