namespace AbioticEditor.Tests;

/// <summary>
/// High-value host contract tests. These guard the server-rendered application shell and
/// local-only actions without relying on a browser driver or a desktop toolkit.
/// </summary>
public sealed class RazorHostAcceptanceTests
{
    [Fact]
    public void Host_registers_local_workspace_import_preferences_and_health_contracts()
    {
        var program = Source("Program.cs");

        Assert.Contains("builder.WebHost.UseUrls(localUrl)", program, StringComparison.Ordinal);
        Assert.Contains("app.MapGet(\"/healthz\"", program, StringComparison.Ordinal);
        Assert.Contains("AddScoped<BrowserSaveImportService>", program, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<HostThemeService>", program, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<HostSpoilerPreferences>", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Layout_exposes_skip_navigation_landmark_and_all_host_routes()
    {
        var layout = Source("Components", "Pages", "MainLayout.razor");

        Assert.Contains("skip-link", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"#main-content\"", layout, StringComparison.Ordinal);
        Assert.Contains("<main id=\"main-content\" tabindex=\"-1\">", layout, StringComparison.Ordinal);
        // The home button and brand link land on browse?home: the world list must show even
        // while a save is open, and a plain "/" would just re-render ModeSelect (it owns that
        // route). Written WITHOUT a leading slash so it resolves against the page's base
        // address - the published browser editor is served from a sub-folder, where "/browse"
        // would leave the editor entirely and land on a 404. The "?home" query flag (not just
        // the "browse" path) is what tells Home.razor's IsBrowsing to force the world list -
        // since ModeSelect claimed "/", "browse" is this page's ONLY route, so the path alone
        // can no longer distinguish "clicked Home" from "picked a save while already here".
        Assert.Contains("href=\"browse?home\"", layout, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"OpenSettings\"", layout, StringComparison.Ordinal);
        Assert.Contains("<Settings OnClose=\"CloseSettings\" />", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("more-menu", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"/compare\"", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_shell_exposes_keyboard_operable_save_lifecycle_and_panes()
    {
        var shell = Source("Components", "Shared", "WorkspaceShell.razor");

        Assert.Contains("L.Text(\"save.workspace\")", shell, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", shell, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"ReloadAsync\"", shell, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"Revert\"", shell, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"SaveAsync\"", shell, StringComparison.Ordinal);
        Assert.Contains("role=\"separator\"", shell, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", shell, StringComparison.Ordinal);
        Assert.Contains("ResizeWithKeyboard", shell, StringComparison.Ordinal);
        Assert.Contains("await Workspace.SelectAsync(save.Path)", shell, StringComparison.Ordinal);
        Assert.Contains("Uri.EscapeDataString(Path.GetFileName(config.FullPath))", shell, StringComparison.Ordinal);
        Assert.Contains("<IniEditor RequestedFileName=\"@iniFileName\" />", shell, StringComparison.Ordinal);
        Assert.Contains("IniSessions.OpenNamedDiscovered(requestedRoot, RequestedFileName!)", Source("Components", "Pages", "IniEditor.razor"), StringComparison.Ordinal);
        Assert.Contains("export function attach", Source("wwwroot", "workspace-shell.js"), StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_and_home_surfaces_keep_the_native_desktop_actions()
    {
        var settings = Source("Components", "Pages", "Settings.razor");
        var home = Source("Components", "Pages", "Home.razor");
        var editor = Source("Components", "Pages", "SaveEditorSurface.razor");
        Assert.Contains("ReloadGameData", settings, StringComparison.Ordinal);
        Assert.Contains("ResealSpoilers", settings, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", settings, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", settings, StringComparison.Ordinal);
        Assert.Contains("HostSpoilerPreferences", Source("Services", "HostSpoilerPreferences.cs"), StringComparison.Ordinal);
        Assert.Contains("OnInitializedAsync", home, StringComparison.Ordinal);
        Assert.Contains("Library.DiscoverAsync", home, StringComparison.Ordinal);
        Assert.Contains("PickFolderAsync", home, StringComparison.Ordinal);
        Assert.Contains("Workspace.Changed += WorkspaceChanged", home, StringComparison.Ordinal);
        Assert.Contains("<SaveEditorSurface", home, StringComparison.Ordinal);
        Assert.Contains("<PlayerEditor", editor, StringComparison.Ordinal);
        Assert.Contains("<WorldFlagsTab", editor, StringComparison.Ordinal);
        Assert.Contains("Workspace.Changed += WorkspaceChanged", editor, StringComparison.Ordinal);
        Assert.Contains("Workspace.Changed -= WorkspaceChanged", editor, StringComparison.Ordinal);
        Assert.Contains("@PlatformLabel(world)", home, StringComparison.Ordinal);
        Assert.DoesNotContain("@world.SourceLabel", home, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"save-picker\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("world-folder", home, StringComparison.Ordinal);
        Assert.DoesNotContain("@onclick=\"DiscoverAsync\"", home, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_facing_copy_does_not_expose_application_architecture()
    {
        var sources = UiSource.EnumerateFiles("Components", "*.razor", SearchOption.AllDirectories)
            .Append(UiSource.Resolve("Services", "DesktopHostService.cs"))
            .Concat(UiSource.EnumerateFiles("Localization", "*.resx"));
        var bannedPhrases = new[]
        {
            "HOST BOUNDARIES",
            "Razor host",
            "LOCAL BLAZOR HOST",
            "Browser fallback",
            "running local web host",
            "Proton prefixes",
            "Steam-managed installs",
            "shared with Core and the CLI",
            "Core validator",
            "through Core validation",
            "retained by Core",
            "component-by-component",
            "environment variable",
            "portable host settings",
            "embedded browser",
            "system browser",
            "in any browser instead",
            "UESAVEGAME · BLAZOR",
        };

        foreach (var source in sources)
        {
            var content = File.ReadAllText(source);
            foreach (var phrase in bannedPhrases)
                Assert.DoesNotContain(phrase, content, StringComparison.OrdinalIgnoreCase);
        }

        var settings = UiSource.ReadAllText("Components", "Pages", "Settings.razor");
        Assert.DoesNotContain("HostDescription", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Manifest.Runtime", settings, StringComparison.Ordinal);
        foreach (var resource in UiSource.EnumerateFiles("Localization", "*.resx"))
            Assert.DoesNotContain("Plugins_WebToolsDesc", File.ReadAllText(resource), StringComparison.Ordinal);

        var implementationTerms = new[]
        {
            "HOST BOUNDARIES",
            "Razor",
            "Blazor",
            "loopback",
            "localhost",
            "embedded browser",
            ".NET runtime",
            "process architecture",
            "runtime identifier",
            "Photino",
            "WebView",
            "ASP.NET",
        };
        var localizedValues = UiSource
            .EnumerateFiles("Localization", "*.resx")
            .SelectMany(resource => System.Xml.Linq.XDocument.Load(resource)
                .Descendants("value")
                .Select(value => value.Value));
        var serviceMessages = QuotedStrings(Source("Services", "DesktopHostService.cs"));
        foreach (var text in localizedValues.Concat(serviceMessages))
        {
            foreach (var term in implementationTerms)
                Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
        }

        // Check literal rendered text separately so framework directives, namespaces, and
        // the Blazor bootstrap script do not create false positives. "Server" is valid only
        // on the INI screen, where players intentionally edit dedicated-server settings.
        foreach (var source in UiSource.EnumerateFiles("Components", "*.razor", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(source);
            Assert.DoesNotMatch(
                new System.Text.RegularExpressions.Regex(@"\b(?:exception|ex)\.Message\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                markup);
            // Razor comments (@* ... *@) are stripped by the compiler and never reach the page,
            // so they are not player-facing copy and must not be scanned as if they were -
            // otherwise a note explaining the markup to the next maintainer fails this test.
            var rendered = System.Text.RegularExpressions.Regex.Replace(
                markup, @"@\*.*?\*@", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            var textNodes = System.Text.RegularExpressions.Regex.Matches(rendered, @">(?<text>[^<]+)<")
                .Select(match => match.Groups["text"].Value)
                .Where(text => !string.IsNullOrWhiteSpace(text));
            foreach (var text in textNodes)
            {
                foreach (var term in implementationTerms)
                    Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
                if (!source.EndsWith("IniEditor.razor", StringComparison.OrdinalIgnoreCase))
                    Assert.DoesNotContain("server", text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IEnumerable<string> QuotedStrings(string source)
        => System.Text.RegularExpressions.Regex.Matches(source, "\\\"(?:\\\\.|[^\\\"\\\\])*\\\"")
            .Select(match => match.Value[1..^1]);

    private static string Source(params string[] parts) => UiSource.ReadAllText(parts);
}
