using AbioticEditor.Core.Saves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The local machine's file system: what the desktop host has always used, now behind
/// <see cref="ISaveFileSystem"/> so the browser host can substitute its own.
/// </summary>
/// <remarks>
/// Deliberately a thin pass-through. Writes go through <see cref="SaveBackup"/> rather than
/// reimplementing the backup-then-atomic-replace dance, so the desktop keeps the exact write
/// behaviour it had before this seam existed.
/// </remarks>
public sealed class DesktopSaveFileSystem : ISaveFileSystem
{
    public bool HasLocalPaths => true;

    public Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default)
        => Task.FromResult(Directory.Exists(folder));

    public Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<SaveFileEntry>>(() =>
        {
            var entries = new List<SaveFileEntry>();
            foreach (var path in Directory.EnumerateFiles(folder, "*.sav", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                entries.Add(new SaveFileEntry(
                    info.FullName,
                    Path.GetRelativePath(folder, info.FullName),
                    info.Name,
                    info.Length));
            }
            return entries;
        }, cancellationToken);

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllBytesAsync(path, cancellationToken);

    public async Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        var buffer = new byte[(int)Math.Min(maxBytes, stream.Length)];
        await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer;
    }

    public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
        => Task.Run(() => SaveBackup.WriteWithBackup(path, stream => stream.Write(contents, 0, contents.Length)), cancellationToken);
}
