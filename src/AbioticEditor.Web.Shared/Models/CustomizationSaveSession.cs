using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// Staged Razor editor for one appearance save. Appearance saves have their own save
/// button because the game stores them outside the selected world: on Steam a
/// <c>ScientistCustomization_&lt;slot&gt;.sav</c> file next to the account's worlds, on
/// Game Pass a <c>ProfileScientistCustomization_&lt;slot&gt;</c> wgs container.
/// </summary>
public sealed class CustomizationSaveSession
{
    private readonly CustomizationSaveFile _file;
    private readonly Dictionary<string, string> _original;
    private readonly GamePassSaveSet? _gamePassSet;
    private readonly int _gamePassSlot;
    private byte[]? _gamePassBlob; // current slot bytes for re-serialization
    private readonly string? _identifier;   // where the bytes came from, when not a local path
    private byte[]? _bytes;                 // the loose-bytes counterpart of _gamePassBlob

    private CustomizationSaveSession(CustomizationSaveFile file, GamePassSaveSet? gamePassSet = null, int gamePassSlot = 0, byte[]? gamePassBlob = null)
    {
        _file = file;
        _gamePassSet = gamePassSet;
        _gamePassSlot = gamePassSlot;
        _gamePassBlob = gamePassBlob;
        Fields = file.Fields.Select(source => new CustomizationFieldEdit(source)).ToList();
        _original = Fields.ToDictionary(edit => edit.PropertyName, edit => edit.Value, StringComparer.OrdinalIgnoreCase);
    }

    private CustomizationSaveSession(CustomizationSaveFile file, string identifier, byte[] bytes)
        : this(file)
    {
        _identifier = identifier;
        _bytes = bytes;
    }

    public string Path => _identifier ?? _file.FilePath;
    public List<CustomizationFieldEdit> Fields { get; }
    public bool IsGamePass => _gamePassSet is not null;
    public bool IsDirty => Fields.Any(edit => !_original.TryGetValue(edit.PropertyName, out var value)
        || !string.Equals(value, edit.Value, StringComparison.Ordinal));

    public static CustomizationSaveSession Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An appearance save path is required.", nameof(path));
        return new CustomizationSaveSession(CustomizationSaveFile.LoadFromFile(System.IO.Path.GetFullPath(path)));
    }

    /// <summary>
    /// Loads an appearance save from bytes the host already read, for a host with no local file
    /// system. <paramref name="identifier"/> is only carried back out through <see cref="Path"/>
    /// so the caller knows where to write; nothing here interprets it.
    /// </summary>
    /// <remarks>
    /// Uses the same byte round-trip the Game Pass path has always used, so an appearance edit
    /// made in a browser goes through exactly the code a Game Pass edit does on the desktop.
    /// </remarks>
    public static CustomizationSaveSession LoadFromBytes(string identifier, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new CustomizationSaveSession(CustomizationSaveFile.LoadFromBytes(bytes), identifier, bytes);
    }

    /// <summary>
    /// True when this session was loaded from bytes and so must be saved with
    /// <see cref="SaveToBytes"/> rather than <see cref="Save"/>.
    /// </summary>
    public bool WritesBytes => _bytes is not null;

    /// <summary>
    /// The updated file contents for a byte-loaded session, for the caller to write back through
    /// its own file system. Marks the staged edits as saved, so call it only when the write that
    /// follows is going to happen.
    /// </summary>
    public byte[] SaveToBytes()
    {
        if (_bytes is null) throw new InvalidOperationException("This appearance save was loaded from a file; use Save().");

        var values = Fields.ToDictionary(edit => edit.PropertyName, edit => edit.Value, StringComparer.OrdinalIgnoreCase);
        _bytes = CustomizationSaveFile.ApplyChanges(_bytes, values);
        foreach (var edit in Fields) _original[edit.PropertyName] = edit.Value;
        return _bytes;
    }

    /// <summary>
    /// Loads slot <paramref name="slot"/> from the Game Pass profile container
    /// (<c>ProfileScientistCustomization_&lt;slot&gt;</c>), or null when the character was
    /// never customized in-game (mirrors the native Game Pass appearance editor).
    /// </summary>
    public static CustomizationSaveSession? LoadGamePass(GamePassSaveSet set, int slot)
    {
        ArgumentNullException.ThrowIfNull(set);
        var blob = set.ReadProfileCustomization(slot);
        return blob is null
            ? null
            : new CustomizationSaveSession(CustomizationSaveFile.LoadFromBytes(blob), set, slot, blob);
    }

    public void Revert()
    {
        foreach (var edit in Fields)
            if (_original.TryGetValue(edit.PropertyName, out var value)) edit.Value = value;
    }

    public void Save()
    {
        var values = Fields.ToDictionary(edit => edit.PropertyName, edit => edit.Value, StringComparer.OrdinalIgnoreCase);
        if (_gamePassSet is not null && _gamePassBlob is not null)
        {
            // Same write path as the native editor: re-apply onto the original blob and
            // hand it to the container store, which backs up the wgs folder on first write.
            var updated = CustomizationSaveFile.ApplyChanges(_gamePassBlob, values);
            _gamePassSet.WriteProfileCustomization(_gamePassSlot, updated);
            _gamePassBlob = updated;
        }
        else
        {
            _file.Save(values);
        }
        foreach (var edit in Fields) _original[edit.PropertyName] = edit.Value;
    }

    /// <summary>
    /// Locates the Xbox wgs container folder an open workspace came from, so a Game Pass
    /// world edited as loose files can still reach its account-level appearance containers.
    /// The conversion flow writes the loose copy beside the container folder as
    /// <c>&lt;wgs&gt;-Steam</c>, so that sibling is probed first; a working copy that lives
    /// inside the wgs folder itself is found through its ancestors. Read-only and
    /// best-effort: returns null when nothing nearby is a wgs container folder.
    /// </summary>
    public static string? TryLocateGamePassStore(string? workspaceFolder)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolder)) return null;
        try
        {
            var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(workspaceFolder));
            const string convertedSuffix = "-Steam";
            if (full.EndsWith(convertedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var sibling = full[..^convertedSuffix.Length];
                if (GamePassSaveSet.IsGamePassFolder(sibling)) return sibling;
            }
            for (var directory = new DirectoryInfo(full); directory is not null; directory = directory.Parent)
                if (GamePassSaveSet.IsGamePassFolder(directory.FullName)) return directory.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Unreadable folder: treat as "not Game Pass".
        }
        return null;
    }

    /// <summary>
    /// The appearance saves belonging to a player, found by walking up to the account folder on
    /// disk. Desktop only - a host with no local paths has nothing to walk, and finds them in the
    /// folder the player opened instead (see the appearance editor).
    /// </summary>
    public static IReadOnlyList<string> DiscoverNearPlayer(string playerPath, string? playerIdentifier)
    {
        if (string.IsNullOrWhiteSpace(playerPath) || string.IsNullOrWhiteSpace(playerIdentifier)) return [];
        var directory = new FileInfo(playerPath).Directory;
        while (directory is not null && !string.Equals(directory.Name, playerIdentifier, StringComparison.OrdinalIgnoreCase))
            directory = directory.Parent;
        return directory is null
            ? []
            : Directory.EnumerateFiles(directory.FullName, "ScientistCustomization_*.sav", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

public sealed class CustomizationFieldEdit(CustomizationField source)
{
    public string PropertyName { get; } = source.PropertyName;
    public string Label { get; } = source.Label;
    public string TableName { get; } = source.TableName;
    public string Value { get; set; } = source.CurrentValue;
}
