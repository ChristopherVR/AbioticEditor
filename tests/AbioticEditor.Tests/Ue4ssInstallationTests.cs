using AbioticEditor.Core.LiveEditing;

namespace AbioticEditor.Tests;

public sealed class Ue4ssInstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "abiotic-ue4ss-check-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("ue4ss")]
    [InlineData("")]
    public void Finds_existing_nested_and_flat_installations(string relativeRoot)
    {
        var runtime = Path.Combine(_root, relativeRoot);
        var shared = Path.Combine(runtime, "Mods", "shared", "UEHelpers");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(runtime, "UE4SS.dll"), "fixture");
        File.WriteAllText(Path.Combine(shared, "UEHelpers.lua"), "fixture");
        Assert.Equal(Path.Combine(runtime, "Mods"), Ue4ssInstallation.FindModsDirectory(_root));
    }

    [Fact]
    public void Incomplete_nested_runtime_does_not_deploy_into_a_different_flat_installation()
    {
        Finds_existing_nested_and_flat_installations("");
        Directory.CreateDirectory(Path.Combine(_root, "ue4ss"));
        File.WriteAllText(Path.Combine(_root, "ue4ss", "UE4SS.dll"), "existing");
        Assert.Null(Ue4ssInstallation.FindModsDirectory(_root));
    }

    [Fact]
    public void Missing_or_incomplete_installation_is_left_unchanged()
    {
        Assert.Null(Ue4ssInstallation.FindModsDirectory(_root));
        Assert.False(Directory.Exists(_root));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "UE4SS.dll"), "existing");
        Assert.Null(Ue4ssInstallation.FindModsDirectory(_root));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(_root, "UE4SS.dll")));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
