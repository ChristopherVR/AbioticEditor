using System.Text.RegularExpressions;

namespace AbioticEditor.Tests;

/// <summary>
/// The app ships a cut-down Material Symbols font (tools/subset-icon-font.py) holding only the icons
/// its sources name, instead of the full 10 MB font. Icons are drawn by ligature, so an icon missing
/// from the cut-down font shows up as its name spelled out in plain letters. This scans the sources
/// the same way the script does and fails when they name an icon the shipped font does not have.
/// </summary>
public sealed class IconFontSubsetTests
{
    private static readonly string[] ScanDirectories = ["src", "plugins"];
    private static readonly HashSet<string> ScanExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".razor", ".cs", ".js", ".mjs", ".css", ".html", ".cshtml", ".ts", ".tsx", ".jsx",
    };
    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "lib", "thumbs", "dist",
    };
    private static readonly Regex Token = new("[a-z0-9_]{2,}", RegexOptions.CultureInvariant);

    [Fact]
    public void Every_icon_the_sources_name_is_in_the_shipped_font()
    {
        var fonts = Path.Combine(UiSource.RepositoryRoot, "assets", "fonts");
        var all = File.ReadAllLines(Path.Combine(fonts, "material-symbols-all.txt")).ToHashSet(StringComparer.Ordinal);
        var kept = File.ReadAllLines(Path.Combine(fonts, "material-symbols-kept.txt")).ToHashSet(StringComparer.Ordinal);

        var missing = SourceTokens().Where(all.Contains).Where(t => !kept.Contains(t)).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0,
            $"The sources name icons the shipped icon font does not have: {string.Join(", ", missing)}. Run `python tools/subset-icon-font.py`.");
    }

    [Fact]
    public void The_shipped_icon_font_is_the_small_cut_down_one()
    {
        var shipped = new FileInfo(Path.Combine(UiSource.RepositoryRoot, "src", "AbioticEditor.Web.Shared", "wwwroot", "fonts", "MaterialSymbolsOutlined.ttf"));

        Assert.True(shipped.Exists);
        Assert.True(shipped.Length < 1024 * 1024, $"The shipped icon font is {shipped.Length / 1024} KB; it should be the cut-down font, not the full one.");
    }

    private static HashSet<string> SourceTokens()
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var top in ScanDirectories)
        {
            var root = Path.Combine(UiSource.RepositoryRoot, top);
            if (Directory.Exists(root)) Collect(root, tokens);
        }
        return tokens;
    }

    private static void Collect(string directory, HashSet<string> tokens)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (!ScanExtensions.Contains(Path.GetExtension(file))) continue;
            foreach (Match match in Token.Matches(File.ReadAllText(file))) tokens.Add(match.Value);
        }
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!SkipDirectories.Contains(Path.GetFileName(child))) Collect(child, tokens);
        }
    }
}
