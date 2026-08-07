namespace AbioticEditor.Tests;

/// <summary>
/// Locates a front-end source file, which may now live in either of two projects.
/// </summary>
/// <remarks>
/// The editor's screens and their supporting services moved into
/// <c>src/AbioticEditor.Web.Shared</c> so the browser host can render the same ones, while a
/// few stayed in <c>src/AbioticEditor.Web</c> because they cannot leave the desktop host (the
/// app shell and router, the bundled updater and plugin web-tool screens, the desktop pickers,
/// and <c>wwwroot</c>). The UI parity tests assert against source text, so they need to find a
/// file wherever it currently lives instead of hardcoding one project - which also means a
/// future move between the two does not break every one of them again.
/// </remarks>
internal static class UiSource
{
    /// <summary>Repository root, found by walking up to the solution file.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static readonly string[] Roots =
    [
        Path.Combine(RepositoryRoot, "src", "AbioticEditor.Web.Shared"),
        Path.Combine(RepositoryRoot, "src", "AbioticEditor.Web"),
    ];

    /// <summary>
    /// Full path of a front-end file given its project-relative parts (either
    /// <c>"Components", "Pages", "Home.razor"</c> or <c>"Components/Pages/Home.razor"</c>).
    /// Returns the path under the first project that has it; when neither does, returns the
    /// shared-library path so the caller's own assertion reports a usable location.
    /// </summary>
    public static string Resolve(params string[] parts)
    {
        var relative = Path.Combine(parts).Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in Roots)
        {
            var candidate = Path.Combine(root, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
        }
        return Path.Combine(Roots[0], relative);
    }

    /// <summary>True when the file exists under either project.</summary>
    public static bool Exists(params string[] parts) => File.Exists(Resolve(parts));

    /// <summary>Reads a front-end source file from whichever project holds it.</summary>
    public static string ReadAllText(params string[] parts) => File.ReadAllText(Resolve(parts));

    /// <summary>
    /// Every match for <paramref name="pattern"/> in <paramref name="relativeDirectory"/> across
    /// both projects, so a folder split between them still enumerates as one set.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(string relativeDirectory, string pattern, SearchOption option = SearchOption.TopDirectoryOnly)
    {
        var relative = relativeDirectory.Replace('/', Path.DirectorySeparatorChar);
        foreach (var root in Roots)
        {
            var directory = Path.Combine(root, relative);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, pattern, option)) yield return file;
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AbioticEditor.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
