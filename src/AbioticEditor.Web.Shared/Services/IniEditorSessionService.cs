using AbioticEditor.Core.Ini;

namespace AbioticEditor.Web.Services;

/// <summary>UI-neutral editable session for one game configuration file.</summary>
public sealed class IniEditorSessionService
{
    public IReadOnlyList<AbioticIniFile> Files { get; private set; } = [];
    public IniDocumentSession? Current { get; private set; }
    public event Action? Changed;

    public void Discover(string path)
    {
        Files = AbioticIniCatalog.Discover(path);
        Current = null;
        Changed?.Invoke();
    }

    public void Open(string path)
    {
        var file = Files.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (file is null) throw new InvalidOperationException("That INI file is not in the current catalog.");
        Current = new IniDocumentSession(file);
        Changed?.Invoke();
    }

    /// <summary>
    /// Rebuilds the catalog and opens a file as one operation. This is used by route-based
    /// navigation so an INI link remains valid after a component or Blazor circuit is
    /// recreated instead of depending on transient component state populated before the
    /// navigation occurred.
    /// </summary>
    public void OpenDiscovered(string discoveryPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var files = AbioticIniCatalog.Discover(discoveryPath);
        var fullPath = Path.GetFullPath(path);
        var file = files.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            throw new InvalidOperationException("That INI file is not in the current catalog.");
        }

        // Publish the new catalog only after the requested document has loaded. A failed
        // link must not destroy the document the user already has open.
        var document = new IniDocumentSession(file);
        Files = files;
        Current = document;
        Changed?.Invoke();
    }

    public void OpenNamedDiscovered(string discoveryPath, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var file = AbioticIniCatalog.Discover(discoveryPath).FirstOrDefault(candidate =>
            string.Equals(Path.GetFileName(candidate.FullPath), Path.GetFileName(fileName), StringComparison.OrdinalIgnoreCase));
        if (file is null)
            throw new InvalidOperationException("That INI file is not in the current catalog.");

        OpenDiscovered(discoveryPath, file.FullPath);
    }
}

/// <summary>Staged editable view over an <see cref="IniFile"/>; writes preserve a .bak copy.</summary>
public sealed class IniDocumentSession
{
    private IniFile _file;

    public IniDocumentSession(AbioticIniFile file)
    {
        File = file;
        _file = IniFile.Load(file.FullPath);
        Sections = BuildSections();
    }

    public AbioticIniFile File { get; }
    public IReadOnlyList<IniSectionDraft> Sections { get; private set; }
    public string FileName => Path.GetFileName(File.FullPath);
    public string KindLabel => AbioticIniCatalog.LabelFor(File.Kind);
    public string Description => AbioticIniCatalog.DescriptionFor(File.Kind);
    public string? StatusResourceKey { get; private set; }
    public bool IsDirty => Sections.Any(s => s.Removed.Count != 0 || s.Entries.Any(e => e.IsDirty));

    public void Save()
    {
        foreach (var section in Sections)
        {
            var target = section.Name.Length == 0
                ? _file.FindSection(null) ?? throw new InvalidOperationException("The unnamed section no longer exists.")
                : _file.GetOrAddSection(section.Name);
            foreach (var removed in section.Removed)
                target.RemoveValue(removed.Key, removed.OriginalValue!);
            section.Removed.Clear();
            foreach (var entry in section.Entries.Where(e => e.IsDirty))
            {
                if (entry.OriginalValue is null) target.AddValue(entry.Key, entry.Value);
                else if (target.GetValues(entry.Key).Count > 1)
                {
                    target.RemoveValue(entry.Key, entry.OriginalValue);
                    target.AddValue(entry.Key, entry.Value);
                }
                else target.SetValue(entry.Key, entry.Value);
                entry.AcceptBaseline();
            }
        }
        System.IO.File.Copy(File.FullPath, File.FullPath + ".bak", overwrite: true);
        _file.Save(File.FullPath);
        StatusResourceKey = "Ini_StatusSaved";
    }

    public void Revert()
    {
        _file = IniFile.Load(File.FullPath);
        Sections = BuildSections();
        StatusResourceKey = "Ini_StatusReloaded";
    }

    private List<IniSectionDraft> BuildSections() => _file.Sections
        .Where(s => s.Name.Length > 0 || s.Entries.Any())
        .Select(s => new IniSectionDraft(s.Name, s.Entries))
        .ToList();
}

public sealed class IniSectionDraft(string name, IEnumerable<KeyValuePair<string, string>> entries)
{
    public string Name { get; } = name;
    public string Header => Name.Length == 0 ? "(no section)" : $"[{Name}]";
    public List<IniEntryDraft> Entries { get; } = entries.Select(e => new IniEntryDraft(e.Key, e.Value)).ToList();
    public List<IniEntryDraft> Removed { get; } = [];
    public string? SuggestedNewKey => string.Equals(Name, AbioticIniCatalog.ModeratorsSection, StringComparison.OrdinalIgnoreCase) ? AbioticIniCatalog.ModeratorKey
        : string.Equals(Name, AbioticIniCatalog.BannedPlayersSection, StringComparison.OrdinalIgnoreCase) ? AbioticIniCatalog.BannedPlayerKey
        : Entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1)?.Key;

    public void Add() { if (SuggestedNewKey is { } key) Entries.Add(new IniEntryDraft(key, string.Empty, true)); }
    public void Remove(IniEntryDraft entry) { if (Entries.Remove(entry) && entry.OriginalValue is not null) Removed.Add(entry); }
}

public sealed class IniEntryDraft(string key, string value, bool isNew = false)
{
    public string Key { get; } = key;
    public string Value { get; set; } = value;
    public string? OriginalValue { get; private set; } = isNew ? null : value;
    public bool IsDirty => OriginalValue is null || !string.Equals(OriginalValue, Value, StringComparison.Ordinal);
    public void AcceptBaseline() => OriginalValue = Value;
}
