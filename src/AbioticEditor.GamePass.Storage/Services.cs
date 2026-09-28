namespace AbioticEditor.GamePass.Storage;

/// <summary>
/// The filesystem operations the container layer needs. Injectable so a store can be exercised
/// against an in-memory or fault-injecting implementation (locked files, interrupted commits).
/// </summary>
public interface IWgsFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    byte[] ReadAllBytes(string path);

    /// <summary>Reads up to <paramref name="count"/> bytes from the start of a file.</summary>
    byte[] ReadHead(string path, int count);

    void WriteAllBytes(string path, byte[] bytes);

    /// <summary>Moves a file, replacing the destination (same-volume atomic replace where the OS offers one).</summary>
    void MoveOverwrite(string source, string destination);

    void DeleteFile(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(string directory);
    IEnumerable<string> EnumerateDirectories(string directory);
    long GetFileLength(string path);
    DateTime GetLastWriteTimeUtc(string path);
}

/// <summary>The real filesystem.</summary>
public sealed class PhysicalWgsFileSystem : IWgsFileSystem
{
    public static PhysicalWgsFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public byte[] ReadHead(string path, int count)
    {
        var buffer = new byte[count];
        using var stream = File.OpenRead(path);
        var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        return buffer.AsSpan(0, read).ToArray();
    }

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void MoveOverwrite(string source, string destination) => File.Move(source, destination, overwrite: true);
    public void DeleteFile(string path) => File.Delete(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public IEnumerable<string> EnumerateFiles(string directory) => Directory.EnumerateFiles(directory);
    public IEnumerable<string> EnumerateDirectories(string directory) => Directory.EnumerateDirectories(directory);
    public long GetFileLength(string path) => new FileInfo(path).Length;
    public DateTime GetLastWriteTimeUtc(string path) => new FileInfo(path).LastWriteTimeUtc;
}

/// <summary>Source of "now" for index and entry timestamps.</summary>
public interface IWgsClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>The system clock.</summary>
public sealed class SystemWgsClock : IWgsClock
{
    public static SystemWgsClock Instance { get; } = new();
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>Diagnostic sink. Implementations must never throw.</summary>
public interface IWgsLog
{
    void Info(string message);
    void Warn(string message);
}

/// <summary>Discards everything.</summary>
public sealed class NullWgsLog : IWgsLog
{
    public static NullWgsLog Instance { get; } = new();
    public void Info(string message) { }
    public void Warn(string message) { }
}

/// <summary>What a game adapter recognised in a blob's leading bytes.</summary>
/// <param name="Label">A human-meaningful identity (for instance the world name), or null.</param>
/// <param name="SuggestedContainerName">The container name to register it under, or null.</param>
public sealed record WgsBlobDescription(string? Label, string? SuggestedContainerName);

/// <summary>
/// Game adapter hook: recognises a game's payload from the first bytes of a blob without
/// decoding it, so orphaned data can be identified on a machine with no codecs installed.
/// </summary>
public interface IWgsBlobInspector
{
    /// <summary>How many leading bytes of the blob <see cref="Inspect"/> needs.</summary>
    int HeadBytes { get; }

    /// <summary>Returns null when the payload is not one this adapter recognises.</summary>
    WgsBlobDescription? Inspect(ReadOnlySpan<byte> head);
}

/// <summary>One reason a write is inadvisable.</summary>
/// <param name="Code">Stable machine-readable identifier (adapter-defined).</param>
/// <param name="Blocking">True when the write must be refused.</param>
/// <param name="Message">Explanation for the user.</param>
public sealed record WgsWriteConcern(string Code, bool Blocking, string Message);

/// <summary>The verdict of a write gate.</summary>
public sealed record WgsWriteAssessment(IReadOnlyList<WgsWriteConcern> Concerns)
{
    public static WgsWriteAssessment Clear { get; } = new(Array.Empty<WgsWriteConcern>());

    public bool CanWrite => !Concerns.Any(c => c.Blocking);

    public string BlockingMessage()
        => string.Join(" ", Concerns.Where(c => c.Blocking).Select(c => c.Message));
}

/// <summary>
/// Decides whether a write may proceed. The default gate (<see cref="WgsWriteGates.Structural"/>)
/// looks only at what the store itself records; a platform layer can add process and lock checks.
/// </summary>
public interface IWgsWriteGate
{
    WgsWriteAssessment Assess(WgsStore store);
}

/// <summary>Built-in write gates.</summary>
public static class WgsWriteGates
{
    /// <summary>Refuses a store with an unresolved cloud conflict or a container in an unsafe state.</summary>
    public static IWgsWriteGate Structural { get; } = new StructuralGate();

    /// <summary>Never refuses. For callers that run their own gate before every write.</summary>
    public static IWgsWriteGate AllowAll { get; } = new AllowAllGate();

    public const string UnresolvedConflict = "unresolved-conflict";
    public const string UnsafeState = "unsafe-state";
    public const string ContradictoryState = "contradictory-state";

    private sealed class AllowAllGate : IWgsWriteGate
    {
        public WgsWriteAssessment Assess(WgsStore store) => WgsWriteAssessment.Clear;
    }

    private sealed class StructuralGate : IWgsWriteGate
    {
        public WgsWriteAssessment Assess(WgsStore store)
        {
            var concerns = new List<WgsWriteConcern>();
            if (store.HasUnresolvedConflicts)
            {
                concerns.Add(new WgsWriteConcern(UnresolvedConflict, true,
                    "The store carries an unresolved cloud conflict; an edit written now can be discarded when the service settles it."));
            }
            if (store.UnsafeStateContainers.Count > 0)
            {
                concerns.Add(new WgsWriteConcern(UnsafeState, true,
                    "These containers are deleted or in a state the format does not define: "
                    + string.Join(", ", store.UnsafeStateContainers) + "."));
            }
            if (store.ContradictoryStateContainers.Count > 0)
            {
                concerns.Add(new WgsWriteConcern(ContradictoryState, false,
                    "These containers' state and ETag disagree (a write corrects them): "
                    + string.Join(", ", store.ContradictoryStateContainers) + "."));
            }
            return new WgsWriteAssessment(concerns);
        }
    }
}

/// <summary>Injectable services for a <see cref="WgsStore"/>. Every member has a real default.</summary>
public sealed class WgsStoreOptions
{
    public static WgsStoreOptions Default { get; } = new();

    public IWgsFileSystem FileSystem { get; init; } = PhysicalWgsFileSystem.Instance;
    public IWgsClock Clock { get; init; } = SystemWgsClock.Instance;
    public IWgsLog Log { get; init; } = NullWgsLog.Instance;

    /// <summary>Game adapter payload recogniser used to label orphaned data. Optional.</summary>
    public IWgsBlobInspector? BlobInspector { get; init; }

    /// <summary>Write gate. Null means <see cref="WgsWriteGates.Structural"/>.</summary>
    public IWgsWriteGate? WriteGate { get; init; }
}
