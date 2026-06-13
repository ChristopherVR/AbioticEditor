using System.Collections.ObjectModel;
using System.ComponentModel;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Ini;

namespace AbioticEditor.App.ViewModels;

/// <summary>One editable key=value line of an ini section.</summary>
public sealed class IniEntryViewModel : INotifyPropertyChanged
{
    private readonly IniSectionViewModel _owner;
    private string _value;

    public IniEntryViewModel(IniSectionViewModel owner, string key, string value, bool isNew = false)
    {
        _owner = owner;
        Key = key;
        OriginalValue = isNew ? null : value;
        _value = value;
    }

    public string Key { get; }

    /// <summary>The value as loaded from disk; null for entries added in the editor.</summary>
    public string? OriginalValue { get; private set; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
            _owner.NotifyEntryChanged();
        }
    }

    public bool IsDirty => OriginalValue is null || !string.Equals(OriginalValue, _value, StringComparison.Ordinal);

    internal void AcceptBaseline()
    {
        OriginalValue = _value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One ini section with its editable entries.</summary>
public sealed class IniSectionViewModel : INotifyPropertyChanged
{
    private readonly IniEditorViewModel _owner;

    public IniSectionViewModel(IniEditorViewModel owner, string name, IEnumerable<KeyValuePair<string, string>> entries)
    {
        _owner = owner;
        Name = name;
        foreach (var (key, value) in entries)
        {
            Entries.Add(new IniEntryViewModel(this, key, value));
        }
    }

    public string Name { get; }
    public string Header => Name.Length == 0 ? "(no section)" : $"[{Name}]";
    public ObservableCollection<IniEntryViewModel> Entries { get; } = new();
    public ObservableCollection<IniEntryViewModel> Removed { get; } = new();

    /// <summary>Key suggested for new rows: the section's duplicate-list key when it has one.</summary>
    public string? SuggestedNewKey =>
        string.Equals(Name, AbioticIniCatalog.ModeratorsSection, StringComparison.OrdinalIgnoreCase) ? AbioticIniCatalog.ModeratorKey
        : string.Equals(Name, AbioticIniCatalog.BannedPlayersSection, StringComparison.OrdinalIgnoreCase) ? AbioticIniCatalog.BannedPlayerKey
        : Entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;

    public bool CanAddEntry => SuggestedNewKey is not null;

    public void AddEntry()
    {
        if (SuggestedNewKey is not { } key) return;
        Entries.Add(new IniEntryViewModel(this, key, string.Empty, isNew: true));
        NotifyEntryChanged();
    }

    public void RemoveEntry(IniEntryViewModel entry)
    {
        if (!Entries.Remove(entry)) return;
        if (entry.OriginalValue is not null)
        {
            Removed.Add(entry);
        }
        NotifyEntryChanged();
    }

    internal void NotifyEntryChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Entries)));
        _owner.NotifyDirtyChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Editor over one UE-style ini file (server Admin.ini, a world's SandboxSettings.ini,
/// or client config). Loads through <see cref="IniFile"/>, which preserves comments,
/// ordering, duplicate keys and line endings; only the values actually changed here are
/// touched on save.
/// </summary>
public sealed class IniEditorViewModel : INotifyPropertyChanged
{
    private IniFile _file;

    public IniEditorViewModel(string path, AbioticIniKind kind)
    {
        FilePath = path;
        Kind = kind;
        _file = IniFile.Load(path);
        Sections = BuildSections();
    }

    public string FilePath { get; }
    public AbioticIniKind Kind { get; }
    public string FileName => Path.GetFileName(FilePath);

    public string KindLabel => AbioticIniCatalog.LabelFor(Kind);

    public string Description => AbioticIniCatalog.DescriptionFor(Kind);

    public IReadOnlyList<IniSectionViewModel> Sections { get; private set; }

    public bool IsDirty => Sections.Any(s =>
        s.Removed.Count > 0 || s.Entries.Any(e => e.IsDirty));

    private string? _statusMessage;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusMessage)));
        }
    }

    public void Save()
    {
        try
        {
            foreach (var section in Sections)
            {
                var target = _file.GetOrAddSection(section.Name);
                foreach (var removed in section.Removed)
                {
                    target.RemoveValue(removed.Key, removed.OriginalValue!);
                    EditorLog.Info("Ini", $"{FileName}: removed {section.Header} {removed.Key}={removed.OriginalValue}");
                }
                section.Removed.Clear();

                foreach (var entry in section.Entries.Where(e => e.IsDirty))
                {
                    if (entry.OriginalValue is null)
                    {
                        target.AddValue(entry.Key, entry.Value);
                        EditorLog.Info("Ini", $"{FileName}: added {section.Header} {entry.Key}={entry.Value}");
                    }
                    else
                    {
                        // Duplicate-keyed lists need value-addressed replacement; plain
                        // keys go through SetValue so single-occurrence keys stay single.
                        if (target.GetValues(entry.Key).Count > 1)
                        {
                            target.RemoveValue(entry.Key, entry.OriginalValue);
                            target.AddValue(entry.Key, entry.Value);
                        }
                        else
                        {
                            target.SetValue(entry.Key, entry.Value);
                        }
                        EditorLog.Info("Ini", $"{FileName}: {section.Header} {entry.Key}: '{entry.OriginalValue}' -> '{entry.Value}'");
                    }
                    entry.AcceptBaseline();
                }
            }

            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            _file.Save(FilePath);
            StatusMessage = $"Saved {FileName} (previous version kept as {FileName}.bak).";
            NotifyDirtyChanged();
        }
        catch (Exception ex)
        {
            EditorLog.Error("Ini", $"Saving {FilePath} failed", ex);
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    public void Revert()
    {
        _file = IniFile.Load(FilePath);
        Sections = BuildSections();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Sections)));
        StatusMessage = $"Reloaded {FileName} from disk.";
        NotifyDirtyChanged();
    }

    private List<IniSectionViewModel> BuildSections()
        => _file.Sections
            .Where(s => s.Name.Length > 0 || s.Entries.Any())
            .Select(s => new IniSectionViewModel(this, s.Name, s.Entries))
            .ToList();

    internal void NotifyDirtyChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirty)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
