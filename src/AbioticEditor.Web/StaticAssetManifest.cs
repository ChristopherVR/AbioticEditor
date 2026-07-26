namespace AbioticEditor.Web;

/// <summary>
/// Finds the static-assets manifest <c>MapStaticAssets</c> needs.
///
/// <para>The manifest lists every served file with its content stamp, and the framework will only
/// read it from a path on disk - the app refuses to start without it. The SDK drops it in the
/// top level of the published folder, next to the executable, where it is the one piece of build
/// plumbing on show in an otherwise tidy download. The build moves it into <c>wwwroot</c>, beside
/// the assets it describes, and this looks in both places.</para>
///
/// <para>The top-level copy still wins when present: that is the shape a plain <c>dotnet run</c>
/// produces during development. If neither exists the default path is returned unchanged, so the
/// framework raises its own clear error rather than this throwing a vaguer one.</para>
/// </summary>
internal static class StaticAssetManifest
{
    private const string FileName = "AbioticEditor.Web.staticwebassets.endpoints.json";

    public static string ResolvePath()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, FileName);
        if (File.Exists(beside)) return beside;

        var tucked = Path.Combine(AppContext.BaseDirectory, "wwwroot", FileName);
        return File.Exists(tucked) ? tucked : beside;
    }
}
