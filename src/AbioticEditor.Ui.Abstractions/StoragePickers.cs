namespace AbioticEditor.Ui;

/// <summary>Lets a host choose files from its native or browser-backed storage UI.</summary>
public interface IFilePicker
{
    /// <summary>Prompts for one file, or returns <see langword="null"/> when the user cancels.</summary>
    Task<PickedFile?> PickFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default);

    /// <summary>Prompts for zero or more files. Cancellation by the user returns an empty list.</summary>
    Task<IReadOnlyList<PickedFile>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Lets a host choose a folder from its native or browser-backed storage UI.</summary>
public interface IFolderPicker
{
    /// <summary>Prompts for one folder, or returns <see langword="null"/> when the user cancels.</summary>
    Task<PickedFolder?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Constraints and display text for a file selection request.</summary>
public sealed record FilePickerRequest
{
    /// <summary>Host-visible title for the picker.</summary>
    public string? Title { get; init; }

    /// <summary>Accepted file types. An empty list permits every type supported by the host.</summary>
    public IReadOnlyList<FileTypeFilter> FileTypes { get; init; } = Array.Empty<FileTypeFilter>();
}

/// <summary>A named set of file extensions accepted by a picker.</summary>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Extensions);

/// <summary>Display text for a folder selection request.</summary>
public sealed record FolderPickerRequest
{
    /// <summary>Host-visible title for the picker.</summary>
    public string? Title { get; init; }
}

/// <summary>A file selected by the user.</summary>
/// <remarks>
/// <see cref="Path"/> can be unavailable for browser sandbox selections. Consumers that need bytes
/// should use <see cref="OpenReadAsync"/> instead of assuming a local file-system path.
/// </remarks>
public sealed record PickedFile(string Name, string? Path, Func<CancellationToken, Task<Stream>> OpenReadAsync);

/// <summary>A folder selected by the user.</summary>
/// <remarks>Hosts that cannot expose folders may return no selection.</remarks>
public sealed record PickedFolder(string Name, string? Path);
