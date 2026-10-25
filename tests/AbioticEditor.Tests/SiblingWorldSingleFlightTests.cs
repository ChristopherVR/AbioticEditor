using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class SiblingWorldSingleFlightTests
{
    private static readonly bool[] ReadingStarted = [true];
    private static readonly bool[] ReadingCompleted = [true, false];
    private static readonly bool[] ReadRetried = [true, false, true, false];
    [Fact]
    public async Task Flags_beds_and_deployables_share_one_inflight_read_even_when_reading_rerenders()
    {
        var files = new ControlledFiles(TinyWorld());
        var service = new SiblingWorldBedService(files);
        Task<IReadOnlySet<string>>? reentrant = null;
        var reading = new List<bool>();
        var loaded = 0;
        service.Reading += busy =>
        {
            reading.Add(busy);
            if (busy) reentrant = service.GetFlagsAsync("world");
        };
        service.Loaded += () => loaded++;

        var flags = service.GetFlagsAsync("world");
        var beds = service.GetBedsAsync("world");
        var deployables = service.GetDeployablesAsync("world");
        Assert.Equal(1, files.ReadCount);
        Assert.NotNull(reentrant);
        files.Release("world");
        await Task.WhenAll(flags, beds, deployables, reentrant);

        Assert.Equal(1, files.ReadCount);
        Assert.Equal(1, files.MaxConcurrentReads);
        Assert.Equal(ReadingCompleted, reading);
        Assert.Equal(1, loaded);
        Assert.Same(await flags, await reentrant);
    }

    [Fact]
    public async Task Canceling_a_waiter_does_not_cancel_the_shared_world_read()
    {
        var files = new ControlledFiles(TinyWorld());
        var service = new SiblingWorldBedService(files);
        var first = service.GetFlagsAsync("world");
        using var cancellation = new CancellationTokenSource();
        var waiter = service.GetBedsAsync("world", cancellationToken: cancellation.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(first.IsCompleted);

        files.Release("world");
        await first;
        await service.GetDeployablesAsync("world");
        Assert.Equal(1, files.ReadCount);
    }

    [Fact]
    public async Task A_failed_read_releases_the_gate_and_can_be_retried()
    {
        var files = new ControlledFiles(TinyWorld()) { FailNextRead = true };
        var service = new SiblingWorldBedService(files);
        var reading = new List<bool>();
        service.Reading += reading.Add;
        var first = service.GetFlagsAsync("world");
        files.Release("world");
        await Assert.ThrowsAsync<IOException>(() => first);

        await service.GetFlagsAsync("world");
        Assert.Equal(2, files.ReadCount);
        Assert.Equal(ReadRetried, reading);
    }

    [Fact]
    public async Task A_new_version_replaces_facts_after_a_shared_read()
    {
        var files = new ControlledFiles(TinyWorld());
        files.Release("world");
        var service = new SiblingWorldBedService(files);
        var original = await service.GetFlagsAsync("world");
        Assert.Same(original, await service.GetFlagsAsync("world"));
        files.Stamp = "changed";
        var replacement = await service.GetFlagsAsync("world");
        Assert.NotSame(original, replacement);
        Assert.Equal(2, files.ReadCount);
    }

    [Fact]
    public async Task Concurrent_editable_session_requests_return_the_same_session()
    {
        var files = new ControlledFiles(TinyWorld());
        var service = new SiblingWorldBedService(files);
        var first = service.GetOrLoadSessionAsync("world");
        var second = service.GetOrLoadSessionAsync("world");
        Assert.Equal(1, files.ReadCount);
        files.Release("world");
        Assert.Same(await first, await second);
        Assert.Equal(1, files.ReadCount);
    }

    [Fact]
    public async Task Busy_status_stays_active_until_all_different_world_reads_finish()
    {
        var files = new ControlledFiles(TinyWorld());
        var service = new SiblingWorldBedService(files);
        var reading = new List<bool>();
        service.Reading += reading.Add;
        var first = service.GetFlagsAsync("first");
        var second = service.GetFlagsAsync("second");
        Assert.Equal(ReadingStarted, reading);
        files.Release("first");
        await first;
        Assert.Equal(ReadingStarted, reading);
        files.Release("second");
        await second;
        Assert.Equal(ReadingCompleted, reading);
        Assert.Equal(2, files.MaxConcurrentReads);
    }

    private static byte[] TinyWorld()
    {
        // Keep the fixture's valid GVAS header but strip every property. Each concurrency test
        // parses only this tiny empty world, never a multi-megabyte Facility object graph.
        Assert.NotNull(Fixtures.CascadeDir);
        var world = WorldSaveReader.ReadFromFile(Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav"));
        world.Raw.Properties!.Clear();
        using var stream = new MemoryStream();
        world.Raw.WriteTo(stream);
        return stream.ToArray();
    }

    private sealed class ControlledFiles(byte[] bytes) : ISaveFileSystem
    {
        private readonly Dictionary<string, TaskCompletionSource> _releases = new(StringComparer.OrdinalIgnoreCase);
        private int _readCount;
        private int _activeReads;
        public bool HasLocalPaths => false;
        public bool CanWrite => false;
        public string Stamp { get; set; } = "original";
        public bool FailNextRead { get; set; }
        public int ReadCount => _readCount;
        public int MaxConcurrentReads { get; private set; }

        public void Release(string path) => ReleaseFor(path).TrySetResult();
        private TaskCompletionSource ReleaseFor(string path)
        {
            lock (_releases)
            {
                if (!_releases.TryGetValue(path, out var release))
                {
                    release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _releases[path] = release;
                }
                return release;
            }
        }

        public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _readCount);
            var active = Interlocked.Increment(ref _activeReads);
            MaxConcurrentReads = Math.Max(MaxConcurrentReads, active);
            try
            {
                await ReleaseFor(path).Task.WaitAsync(cancellationToken);
                if (FailNextRead)
                {
                    FailNextRead = false;
                    throw new IOException("Controlled first read failure.");
                }
                return bytes;
            }
            finally { Interlocked.Decrement(ref _activeReads); }
        }

        public Task<string?> GetVersionStampAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(Stamp);
        public Task<bool> FolderExistsAsync(string folder, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<SaveFileEntry>> ListSavesAsync(string folder, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<byte[]> ReadHeaderAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<byte[]> ReadTailAsync(string path, int maxBytes, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WriteAllBytesAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
