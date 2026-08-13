using System.Xml.Linq;

namespace AbioticEditor.Tests;

/// <summary>
/// The browser build publishes with the trimmer in full mode, which is what keeps the download
/// to roughly half of what it used to be. These tests pin the parts of that setup that cannot
/// fail loudly.
/// </summary>
/// <remarks>
/// Full trimming deletes code nothing appears to call. Save files are parsed by reading a
/// property's type name out of the file and building that type by name, so almost none of the
/// save engine is "called" in a way a trimmer can see. Measured, unrooted full trim deleted
/// UeSaveGame and UeSaveGame.Json outright and cut Core from 1007 KB to 268 KB, and rooting the
/// browser project itself was needed too - without it the records handed to JavaScript lost
/// their constructors and the very first click, OPEN FOLDER, failed.
///
/// None of that produces a build error. It produces an editor that ships, starts, looks correct
/// and then cannot open a save, so these roots are asserted here rather than trusted to review.
/// See docs/PROGRESS.md round-58 for the measurements and the browser round-trip that verified it.
/// </remarks>
public sealed class BrowserTrimmingTests
{
    private static XDocument BrowserProject()
    {
        var path = Path.Combine(
            UiSource.RepositoryRoot, "src", "AbioticEditor.Web.Wasm", "AbioticEditor.Web.Wasm.csproj");
        Assert.True(File.Exists(path), $"Could not find the browser project at {path}.");
        return XDocument.Load(path);
    }

    [Fact]
    public void BrowserBuild_TrimsInFullMode()
    {
        var mode = BrowserProject().Descendants("TrimMode").Select(e => e.Value.Trim()).LastOrDefault();

        Assert.True(
            string.Equals(mode, "full", StringComparison.OrdinalIgnoreCase),
            "The browser project must set <TrimMode>full</TrimMode>. Blazor already publishes trimmed, "
            + "but its default 'partial' only touches assemblies whose authors marked them trimmable, "
            + $"and none of ours are - so the download roughly doubles. Found: '{mode}'.");
    }

    [Theory]
    // The save engine: builds property and struct types by name, so a trimmer sees them as dead.
    [InlineData("UeSaveGame")]
    [InlineData("UeSaveGame.Json")]
    // Ours: the save classes the engine looks up by name live here, alongside the screens.
    [InlineData("AbioticEditor.Core")]
    [InlineData("AbioticEditor.Web.Shared")]
    // The browser host: its records are built by the JSON reader when JavaScript answers back.
    [InlineData("AbioticEditor.Web.Wasm")]
    // The picker requests/results, which travel the same way. Full trim also strips constructor
    // parameter names, so leaving this out broke asking for a file before the chooser appeared.
    [InlineData("AbioticEditor.Ui.Abstractions")]
    public void BrowserBuild_KeepsReflectionDrivenAssembliesWhole(string assembly)
    {
        var roots = BrowserProject()
            .Descendants("TrimmerRootAssembly")
            .Select(e => e.Attribute("Include")?.Value?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .ToList();

        Assert.True(
            roots.Contains(assembly, StringComparer.OrdinalIgnoreCase),
            $"'{assembly}' must stay in the browser project's <TrimmerRootAssembly> list. Without it the "
            + "trimmer removes types that are only ever built by name, and the published editor opens "
            + "with no error and then fails on a real save.");
    }

    /// <summary>
    /// Every service a shared screen asks for must be registered by the browser host too.
    /// </summary>
    /// <remarks>
    /// The two hosts share their screens but register their own services, and nothing checks that
    /// the second list keeps up with the first. A service added for the desktop and injected into a
    /// shared component does not fail to compile and does not fail on the desktop; it fails in the
    /// browser, at the moment that screen first renders. When the screen is the start screen, that
    /// is the whole editor. This is exactly how a Game Pass guard written for the desktop took the
    /// browser's landing page down.
    /// </remarks>
    [Fact]
    public void BrowserHost_RegistersEveryServiceTheSharedScreensInject()
    {
        var root = UiSource.RepositoryRoot;
        var wasmProgram = File.ReadAllText(
            Path.Combine(root, "src", "AbioticEditor.Web.Wasm", "Program.cs"));

        var componentsDir = Path.Combine(root, "src", "AbioticEditor.Web.Shared", "Components");
        var injected = Directory
            .EnumerateFiles(componentsDir, "*.razor", SearchOption.AllDirectories)
            .SelectMany(file => System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(file), @"@inject\s+([\w.]+)\s")
                .Select(match => match.Groups[1].Value))
            .Select(name => name[(name.LastIndexOf('.') + 1)..])
            // Blazor registers these itself, so a host never lists them.
            .Where(name => name is not ("NavigationManager" or "IJSRuntime" or "HttpClient"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(injected);

        var missing = injected
            .Where(name => !wasmProgram.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            missing.Count == 0,
            "The browser host does not register these services, which shared screens inject: "
            + string.Join(", ", missing)
            + ". Add them to src/AbioticEditor.Web.Wasm/Program.cs. A service that is only registered "
            + "on the desktop throws in the browser the first time its screen renders.");
    }
}
