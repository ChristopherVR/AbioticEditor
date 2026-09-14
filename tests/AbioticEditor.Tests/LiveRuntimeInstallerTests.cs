using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AbioticEditor.Core.LiveEditing;

namespace AbioticEditor.Tests;

public sealed class LiveRuntimeInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "abiotic-runtime-test-" + Guid.NewGuid().ToString("N"));

    public LiveRuntimeInstallerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Fresh_setup_installs_verified_runtime_without_enabling_sample_mods()
    {
        using var http = Client(Package());
        await new LiveRuntimeInstaller(http).InstallAsync(_root);
        Assert.True(LiveRuntimeInstaller.IsInstalled(_root));
        Assert.True(File.Exists(Path.Combine(_root, "dwmapi.dll")));
        Assert.False(Directory.Exists(Path.Combine(_root, "ue4ss", "Mods", "ConsoleEnablerMod")));
        Assert.Empty(File.ReadAllText(Path.Combine(_root, "ue4ss", "Mods", "mods.txt")));
        Assert.Empty(Directory.GetDirectories(_root, ".abiotic-live-setup-*"));
    }

    [Fact]
    public async Task Unverified_download_never_touches_the_game()
    {
        using var http = Client(Package(), invalidDigest: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveRuntimeInstaller(http).InstallAsync(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("ue4ss/../../outside.dll")]
    [InlineData("C:/outside.dll")]
    public async Task Unsafe_archive_paths_are_rejected_before_install(string path)
    {
        using var http = Client(Package(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => new LiveRuntimeInstaller(http).InstallAsync(_root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("dwmapi.dll")]
    [InlineData("UE4SS.dll")]
    [InlineData("override.txt")]
    public async Task Existing_mod_loader_files_are_preserved(string file)
    {
        File.WriteAllText(Path.Combine(_root, file), "existing mod");
        using var http = Client(Package());
        await Assert.ThrowsAsync<IOException>(() => new LiveRuntimeInstaller(http).InstallAsync(_root));
        Assert.Equal("existing mod", File.ReadAllText(Path.Combine(_root, file)));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task Existing_complete_runtime_needs_no_download_or_replacement()
    {
        using var http = Client(Package());
        var installer = new LiveRuntimeInstaller(http);
        await installer.InstallAsync(_root);
        File.WriteAllText(Path.Combine(_root, "ue4ss", "UE4SS-settings.ini"), "custom settings");
        using var offlineHttp = new HttpClient(new DownloadHandler([], true));
        await new LiveRuntimeInstaller(offlineHttp).InstallAsync(_root);
        Assert.Equal("custom settings", File.ReadAllText(Path.Combine(_root, "ue4ss", "UE4SS-settings.ini")));
    }

    private static HttpClient Client(byte[] package, bool invalidDigest = false) => new(new DownloadHandler(package, invalidDigest));

    private static byte[] Package(string? extra = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            string[] names = ["dwmapi.dll", "ue4ss/UE4SS.dll", "ue4ss/LICENSE", "ue4ss/UE4SS-settings.ini",
                "ue4ss/Mods/shared/UEHelpers/UEHelpers.lua", "ue4ss/Mods/ConsoleEnablerMod/Scripts/main.lua"];
            foreach (var name in extra is null ? names : names.Append(extra))
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("test fixture, not an executable");
            }
        }
        return buffer.ToArray();
    }

    private sealed class DownloadHandler(byte[] package, bool invalidDigest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == LiveRuntimeInstaller.ReleaseEndpoint)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { assets = new[] { new
                    {
                        name = "UE4SS_test.zip", size = package.Length,
                        browser_download_url = "https://github.com/UE4SS-RE/RE-UE4SS/releases/download/test/UE4SS_test.zip",
                        digest = "sha256:" + (invalidDigest ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(package))),
                    } } }),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) });
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
