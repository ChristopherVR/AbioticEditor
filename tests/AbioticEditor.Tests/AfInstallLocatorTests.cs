using System;
using System.IO;
using System.Linq;
using AbioticEditor.Core.Assets;

namespace AbioticEditor.Tests;

/// <summary>
/// Covers the tolerant path resolution that lets a user point the editor at a non-Steam /
/// moved Abiotic Factor install (the fix for an empty TRADERS tab when auto-detection fails).
/// </summary>
public sealed class AfInstallLocatorTests : IDisposable
{
    private readonly string _root;

    public AfInstallLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "afloc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        AfInstallLocator.OverrideInstallRoot = null;
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string MakePaks(params string[] segments)
    {
        var paks = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(paks);
        File.WriteAllText(Path.Combine(paks, "pakchunk0.pak"), "not really a pak");
        return paks;
    }

    [Fact]
    public void ResolvePaksDirectory_AcceptsInstallRoot()
    {
        var paks = MakePaks("AbioticFactor", "Content", "Paks");
        Assert.Equal(paks, AfInstallLocator.ResolvePaksDirectory(_root));
    }

    [Fact]
    public void ResolvePaksDirectory_AcceptsInnerGameFolder()
    {
        var paks = MakePaks("Content", "Paks");
        var inner = Path.Combine(_root); // _root already plays the AbioticFactor folder
        Assert.Equal(paks, AfInstallLocator.ResolvePaksDirectory(inner));
    }

    [Fact]
    public void ResolvePaksDirectory_AcceptsPaksFolderItself()
    {
        var paks = MakePaks("SomeWhere", "Paks");
        Assert.Equal(paks, AfInstallLocator.ResolvePaksDirectory(paks));
    }

    [Fact]
    public void ResolvePaksDirectory_RejectsFolderWithoutPaks()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Null(AfInstallLocator.ResolvePaksDirectory(empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolvePaksDirectory_RejectsBlank(string? path)
        => Assert.Null(AfInstallLocator.ResolvePaksDirectory(path));

    [Fact]
    public void FindPaksDirectory_OverrideWins()
    {
        var paks = MakePaks("AbioticFactor", "Content", "Paks");
        AfInstallLocator.OverrideInstallRoot = _root;
        Assert.Equal(paks, AfInstallLocator.FindPaksDirectory());
    }

    [Fact]
    public void FindPaksDirectory_IgnoresUnusableOverride()
    {
        // A stale/garbage override must fall through to other sources, not hard-fail.
        AfInstallLocator.OverrideInstallRoot = Path.Combine(_root, "does", "not", "exist");
        // Result depends on the host (Steam/env/store), but the call must not throw and must
        // not return the bogus override.
        var result = AfInstallLocator.FindPaksDirectory();
        Assert.NotEqual(AfInstallLocator.OverrideInstallRoot, result);
    }
}
