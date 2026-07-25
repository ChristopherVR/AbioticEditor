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

    private CustomizationSaveSession(CustomizationSaveFile file, GamePassSaveSet? gamePassSet = null, int gamePassSlot = 0, byte[]? gamePassBlob = null)
    {
        _file = file;
        _gamePassSet = gamePassSet;
        _gamePassSlot = gamePassSlot;
        _gamePassBlob = gamePassBlob;
        Fields = file.Fields.Select(source => new CustomizationFieldEdit(source)).ToList();
        _original = Fields.ToDictionary(edit => edit.PropertyName, edit => edit.Value, StringComparer.OrdinalIgnoreCase);
    }

    public string Path => _file.FilePath;
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
