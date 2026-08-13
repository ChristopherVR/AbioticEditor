using AbioticEditor.Core.Saves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Finds the worlds already installed on this machine, for the "worlds found on this machine"
/// list on the start screen.
/// </summary>
/// <remarks>
/// This works by scanning the game's known save locations, which only means anything on a host
/// that can see the local disk. A browser cannot: it only ever sees a folder the player picked
/// and granted access to. Rather than scan a virtual file system that will never contain a
/// world, both methods return nothing there, and the start screen shows its "pick a folder"
/// path instead of an empty list that looks like a failed search.
/// </remarks>
public sealed class SaveLibraryService(ISaveFileSystem files)
{
    /// <summary>True when this host can look for worlds on its own; false in the browser.</summary>
    public bool CanDiscover => files.HasLocalPaths;

    public Task<IReadOnlyList<DiscoveredWorld>> DiscoverAsync(CancellationToken cancellationToken = default)
        => CanDiscover
            ? Task.Run(SaveDiscovery.DiscoverAll, cancellationToken)
            : Task.FromResult<IReadOnlyList<DiscoveredWorld>>(Array.Empty<DiscoveredWorld>());

    /// <summary>
    /// The player accounts this machine has some trace of, offered as a shortcut wherever the
    /// editor asks for one. Nothing in a browser, for the same reason as the world list.
    /// </summary>
    public Task<IReadOnlyList<DiscoveredAccount>> DiscoverAccountsAsync(CancellationToken cancellationToken = default)
        => CanDiscover
            ? Task.Run(PlayerAccountDiscovery.DiscoverAll, cancellationToken)
            : Task.FromResult<IReadOnlyList<DiscoveredAccount>>(Array.Empty<DiscoveredAccount>());

    public Task<IReadOnlyList<SaveFile>> ListFilesAsync(string worldPath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<SaveFile>>(() =>
        {
            if (!CanDiscover) return Array.Empty<SaveFile>();
            if (string.IsNullOrWhiteSpace(worldPath) || !Directory.Exists(worldPath)) return Array.Empty<SaveFile>();
            return Directory.EnumerateFiles(worldPath, "*.sav", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderBy(file => SortOrder(file.Name)).ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Select(file => new SaveFile(file.FullName, file.Name, file.Length, KindFor(file.Name))).ToArray();
        }, cancellationToken);

    private static int SortOrder(string name) => KindFor(name) switch { "META" => 0, "PLAYER" => 1, "WORLD" => 2, _ => 3 };
    private static string KindFor(string name) => name.Equals("WorldSave_MetaData.sav", StringComparison.OrdinalIgnoreCase) ? "META"
        : name.StartsWith("Player_", StringComparison.OrdinalIgnoreCase) ? "PLAYER"
        : name.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase) ? "WORLD" : "SAVE";
}

public sealed record SaveFile(string Path, string Name, long Length, string Kind)
{
    public string Size => Length switch { < 1024 => $"{Length} B", < 1024 * 1024 => $"{Length / 1024d:0.0} KB", _ => $"{Length / 1024d / 1024d:0.0} MB" };
}
