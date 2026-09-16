using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AbioticEditor.Core.LiveEditing;

namespace AbioticEditor.Tests;

public sealed class Ue4ssBundledRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "abiotic-ue4ss-win64-" + Guid.NewGuid().ToString("N"));
    private readonly string _bundleDir = Path.Combine(Path.GetTempPath(), "abiotic-ue4ss-bundle-" + Guid.NewGuid().ToString("N"));

    public Ue4ssBundledRuntimeTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_bundleDir);
    }

    [Fact]
    public async Task Fresh_install_lays_out_runtime_without_sample_mods()
    {
        var runtime = WriteBundle(Package());
        await runtime.InstallAsync(_root);

        Assert.True(Ue4ssBundledRuntime.IsInstalled(_root));
        Assert.True(File.Exists(Path.Combine(_root, "dwmapi.dll")));
        Assert.True(File.Exists(Path.Combine(_root, "ue4ss", "UE4SS.dll")));
        Assert.True(File.Exists(Path.Combine(_root, "ue4ss", "LICENSE")));
        Assert.True(File.Exists(Path.Combine(_root, "ue4ss", "UE4SS-settings.ini")));
        Assert.True(File.Exists(Path.Combine(_root, "ue4ss", "Mods", "shared", "UEHelpers", "UEHelpers.lua")));
        Assert.Empty(File.ReadAllText(Path.Combine(_root, "ue4ss", "Mods", "mods.txt")));
        Assert.False(Directory.Exists(Path.Combine(_root, "ue4ss", "Mods", "ConsoleEnablerMod")));
        Assert.Empty(Directory.GetDirectories(_root, ".abiotic-live-setup-*"));
    }

    [Fact]
    public async Task Wrong_checksum_in_manifest_throws_and_leaves_target_untouched()
    {
        var package = Package();
        var manifestPath = Path.Combine(_bundleDir, "runtime.json");
        var packagePath = Path.Combine(_bundleDir, "UE4SS.zip");
        File.WriteAllBytes(packagePath, package);
        File.WriteAllText(manifestPath, ManifestJson(package.Length, new string('0', 64)));

        var runtime = Ue4ssBundledRuntime.TryLoad(_bundleDir);
        Assert.NotNull(runtime);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime!.InstallAsync(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task Wrong_size_in_manifest_throws_and_leaves_target_untouched()
    {
        var package = Package();
        var manifestPath = Path.Combine(_bundleDir, "runtime.json");
        var packagePath = Path.Combine(_bundleDir, "UE4SS.zip");
        File.WriteAllBytes(packagePath, package);
        var digest = Convert.ToHexString(SHA256.HashData(package));
        File.WriteAllText(manifestPath, ManifestJson(package.Length + 1, digest));

        var runtime = Ue4ssBundledRuntime.TryLoad(_bundleDir);
        Assert.NotNull(runtime);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime!.InstallAsync(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("ue4ss/../../outside.dll")]
    [InlineData("C:/outside.dll")]
    public async Task Unsafe_archive_paths_are_rejected_before_install(string path)
    {
        var runtime = WriteBundle(Package(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.InstallAsync(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("dwmapi.dll")]
    [InlineData("UE4SS.dll")]
    [InlineData("xinput1_3.dll")]
    [InlineData("override.txt")]
    public async Task Existing_mod_loader_traces_are_preserved(string file)
    {
        File.WriteAllText(Path.Combine(_root, file), "existing mod");
        var runtime = WriteBundle(Package());
        await Assert.ThrowsAsync<IOException>(() => runtime.InstallAsync(_root));
        Assert.Equal("existing mod", File.ReadAllText(Path.Combine(_root, file)));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task Existing_complete_runtime_is_left_alone()
    {
        var runtime = WriteBundle(Package());
        await runtime.InstallAsync(_root);
        File.WriteAllText(Path.Combine(_root, "ue4ss", "UE4SS-settings.ini"), "custom settings");

        await runtime.InstallAsync(_root);
        Assert.Equal("custom settings", File.ReadAllText(Path.Combine(_root, "ue4ss", "UE4SS-settings.ini")));
    }

    [Fact]
    public void TryLoad_returns_null_when_zip_is_missing()
    {
        File.WriteAllText(Path.Combine(_bundleDir, "runtime.json"), ManifestJson(1, new string('0', 64)));
        Assert.Null(Ue4ssBundledRuntime.TryLoad(_bundleDir));
    }

    [Fact]
    public void TryLoad_returns_null_when_manifest_is_missing()
    {
        File.WriteAllBytes(Path.Combine(_bundleDir, "UE4SS.zip"), Package());
        Assert.Null(Ue4ssBundledRuntime.TryLoad(_bundleDir));
    }

    [Fact]
    public async Task Ue4ssInstallation_finds_the_installed_mods_directory()
    {
        var runtime = WriteBundle(Package());
        await runtime.InstallAsync(_root);
        Assert.NotNull(Ue4ssInstallation.FindModsDirectory(_root));
    }

    private Ue4ssBundledRuntime WriteBundle(byte[] package)
    {
        var packagePath = Path.Combine(_bundleDir, "UE4SS.zip");
        File.WriteAllBytes(packagePath, package);
        var digest = Convert.ToHexString(SHA256.HashData(package));
        File.WriteAllText(Path.Combine(_bundleDir, "runtime.json"), ManifestJson(package.Length, digest));

        var runtime = Ue4ssBundledRuntime.TryLoad(_bundleDir);
        Assert.NotNull(runtime);
        return runtime!;
    }

    private static string ManifestJson(long size, string sha256) => JsonSerializer.Serialize(new
    {
        name = "UE4SS (RE-UE4SS)",
        version = "v-test",
        asset = "UE4SS_test.zip",
        url = "https://github.com/UE4SS-RE/RE-UE4SS/releases/download/test/UE4SS_test.zip",
        sha256,
        size,
        license = "MIT",
        source = "https://github.com/UE4SS-RE/RE-UE4SS",
        notes = "test fixture",
    });

    private static byte[] Package(string? extra = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            string[] names =
            [
                "dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/LICENSE", "ue4ss/UE4SS-settings.ini",
                "ue4ss/Mods/shared/UEHelpers/UEHelpers.lua", "ue4ss/Mods/ConsoleEnablerMod/Scripts/main.lua",
            ];
            foreach (var name in extra is null ? names : names.Append(extra))
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("test fixture, not an executable");
            }
        }
        return buffer.ToArray();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_bundleDir, recursive: true); } catch (IOException) { }
    }
}
