using System.Text.Json;

namespace AbioticEditor.Web.Services;

/// <summary>Persists the local host's non-sensitive workspace pane preferences.</summary>
public sealed class ShellPreferencesService
{
    private readonly string _path;
    public ShellPreferencesService() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", "web-shell.json")) { }
    public ShellPreferencesService(string path) { _path = path; State = Read(path); }

    public ShellPreferences State { get; private set; }
    public event Action? Changed;

    // Clamp bounds mirror the native ResponsivePaneController's splitter limits
    // (file pane 220-600, slot editor pane 260-680).
    public void SetFilePaneWidth(int width) => Update(State with { FilePaneWidth = Math.Clamp(width, 220, 600) });
    public void SetDetailsPaneWidth(int width) => Update(State with { DetailsPaneWidth = Math.Clamp(width, 260, 680) });
    public void ToggleFilePane() => Update(State with { FilePaneCollapsed = !State.FilePaneCollapsed });
    public void ToggleDetailsPane() => Update(State with { DetailsPaneCollapsed = !State.DetailsPaneCollapsed });

    private void Update(ShellPreferences state)
    {
        State = state;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(state));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    private static ShellPreferences Read(string path)
    {
        try
        {
            var state = File.Exists(path) ? JsonSerializer.Deserialize<ShellPreferences>(File.ReadAllText(path)) : null;
            return state is null ? ShellPreferences.Default : state with
            {
                FilePaneWidth = Math.Clamp(state.FilePaneWidth, 220, 600),
                DetailsPaneWidth = Math.Clamp(state.DetailsPaneWidth, 260, 680),
            };
        }
        catch (IOException) { return ShellPreferences.Default; }
        catch (UnauthorizedAccessException) { return ShellPreferences.Default; }
        catch (JsonException) { return ShellPreferences.Default; }
    }
}

public sealed record ShellPreferences(int FilePaneWidth, int DetailsPaneWidth, bool FilePaneCollapsed, bool DetailsPaneCollapsed)
{
    public static ShellPreferences Default { get; } = new(340, 400, false, false);
}
