using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class WorldLevelCacheTests
{
    [Fact]
    public async Task Canceling_one_waiter_does_not_cancel_shared_scan()
    {
        var files = new BlockingFiles();
        var service = new WorldLevelIndexService(files);
        var workspace = Workspace();
        using var cancel = new CancellationTokenSource();
        var first = service.GetLevelsAsync(workspace, cancel.Token);
        var second = service.GetLevelsAsync(workspace);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        files.Release.TrySetResult([]);
        Assert.Empty(await second);
        Assert.Empty(await service.GetLevelsAsync(workspace));
        Assert.Equal(1, files.Reads);
        Assert.False(files.ScanToken.CanBeCanceled);
    }

    [Fact]
    public async Task Reopening_same_folder_reads_again_but_selection_changes_reuse_scan()
    {
        var files = new BlockingFiles();
        files.Release.TrySetResult([]);
        var service = new WorldLevelIndexService(files);
        var workspace = Workspace();
        await service.GetLevelsAsync(workspace);
        await service.GetLevelsAsync(workspace with { SelectedSave = workspace.Saves[0] });
        Assert.Equal(1, files.Reads);
        await service.GetLevelsAsync(Workspace());
        Assert.Equal(2, files.Reads);
    }

    private static SaveWorkspace Workspace() => new("world", new WorkspaceSave[]
    {
        new("region.sav", "region.sav", "region.sav", 1, SaveDocumentKind.World, null)
    }, null, null, null, null, default, null);

    private sealed class BlockingFiles : ISaveFileSystem
    {
        public TaskCompletionSource<byte[]> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads;
        public CancellationToken ScanToken;
        public bool HasLocalPaths => false;
        public bool CanWrite => false;
        public Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Reads);
            ScanToken = cancellationToken;
            return Release.Task.WaitAsync(cancellationToken);
        }
        public Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string?> GetVersionStampAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
