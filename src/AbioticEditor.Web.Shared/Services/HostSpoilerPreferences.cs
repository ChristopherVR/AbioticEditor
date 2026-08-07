using System.Text.Json;

namespace AbioticEditor.Web.Services;

/// <summary>Local Razor-host equivalent of the desktop spoiler preference and reseal action.</summary>
public sealed class HostSpoilerPreferences
{
    private readonly string _path;
    private readonly HashSet<string> _revealed;
    private bool _enabled;

    public HostSpoilerPreferences() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "webspoilers.json")) { }
    public HostSpoilerPreferences(string path)
    {
        _path = path;
        var stored = Read(path);
        _enabled = stored.Enabled;
        _revealed = stored.Revealed.ToHashSet(StringComparer.Ordinal);
    }

    public bool Enabled { get => _enabled; set { if (_enabled != value) { _enabled = value; Save(); } } }
    public int RevealedCount => _revealed.Count;
    public bool IsRevealed(string key) => _revealed.Contains(key);
    public void Reveal(string key) { if (_revealed.Add(key)) Save(); }
    public void Reseal() { if (_revealed.Count != 0) { _revealed.Clear(); Save(); } }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(new Stored(_enabled, _revealed.OrderBy(key => key, StringComparer.Ordinal).ToArray())));
    }

    private static Stored Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(path)) ?? new(true, []) : new(true, []); }
        catch (IOException) { return new(true, []); }
        catch (UnauthorizedAccessException) { return new(true, []); }
        catch (JsonException) { return new(true, []); }
    }

    private sealed record Stored(bool Enabled, IReadOnlyList<string> Revealed);
}
