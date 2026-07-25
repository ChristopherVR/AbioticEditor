using AbioticEditor.Core.Saves;

namespace AbioticEditor.Web.Services;

public sealed class SaveLibraryService
{
    public Task<IReadOnlyList<DiscoveredWorld>> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.Run(SaveDiscovery.DiscoverAll, cancellationToken);

    public Task<IReadOnlyList<SaveFile>> ListFilesAsync(string worldPath, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<SaveFile>>(() =>
        {
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
