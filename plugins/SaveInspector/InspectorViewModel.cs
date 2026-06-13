using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;
using Microsoft.Maui.Controls;

namespace AbioticEditor.Samples.SaveInspector;

/// <summary>
/// View-model for <see cref="InspectorView"/>. A plain INotifyPropertyChanged class - no
/// MVVM framework needed - exposing a headline summary, a bindable list of skill rows, and
/// a refresh command. It reacts to <see cref="IEditorToolContext.ActiveSaveChanged"/> so the
/// panel keeps in step with whatever save the editor has open.
/// </summary>
public sealed class InspectorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IEditorToolContext _context;
    private string _summary = string.Empty;
    private string _fileName = "(no save open)";
    private bool _hasSkills;
    private bool _showEmptyState;

    public InspectorViewModel(IEditorToolContext context)
    {
        _context = context;
        RefreshCommand = new Command(Refresh);
        _context.ActiveSaveChanged += OnActiveSaveChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Skill rows for the open player save (empty for other kinds).</summary>
    public ObservableCollection<SkillRow> Skills { get; } = new();

    /// <summary>Re-reads the active save and rebuilds the view-model state.</summary>
    public ICommand RefreshCommand { get; }

    public string FileName
    {
        get => _fileName;
        private set => Set(ref _fileName, value);
    }

    public string Summary
    {
        get => _summary;
        private set => Set(ref _summary, value);
    }

    /// <summary>True when there are skill rows to show (the CollectionView's visibility).</summary>
    public bool HasSkills
    {
        get => _hasSkills;
        private set => Set(ref _hasSkills, value);
    }

    /// <summary>True when a save is open but has no skill rows (the empty-state label).</summary>
    public bool ShowEmptyState
    {
        get => _showEmptyState;
        private set => Set(ref _showEmptyState, value);
    }

    private void OnActiveSaveChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>
    /// Detaches from the host context so this view-model doesn't outlive its panel. The host
    /// disposes the view's BindingContext when the tool page closes; even though disposing the
    /// context also severs the event, unsubscribing here is the correct symmetric pattern.
    /// </summary>
    public void Dispose() => _context.ActiveSaveChanged -= OnActiveSaveChanged;

    private void Refresh()
    {
        Skills.Clear();

        var save = _context.ActiveSave;
        FileName = _context.ActiveSavePath is { } p ? Path.GetFileName(p) : "(no save open)";

        if (save is null)
        {
            Summary = "Open a save in the editor to inspect it here.";
            HasSkills = false;
            ShowEmptyState = false;
            return;
        }

        var kind = _context.ActiveSaveKind ?? SaveKind.Any;
        if (kind != SaveKind.Player)
        {
            Summary = $"{kind} save · {save.Properties?.Count ?? 0} top-level properties. "
                + "Skill breakdown is only available for player saves.";
            HasSkills = false;
            ShowEmptyState = true;
            return;
        }

        try
        {
            var data = PlayerSaveReader.ReadFrom(save);
            var names = SkillCatalog.Fallback;
            foreach (var skill in data.Skills.OrderByDescending(s => s.Level))
            {
                var name = skill.Index >= 0 && skill.Index < names.Count
                    ? names[skill.Index].DisplayName
                    : $"Skill {skill.Index}";
                Skills.Add(new SkillRow(name, skill.Level, skill.Xp));
            }
            var top = data.Skills.Count == 0 ? 0 : data.Skills.Max(s => s.Level);
            Summary = $"Money {data.Stats.Money.ToString(CultureInfo.InvariantCulture)} · "
                + $"{data.Skills.Count} skills (top level {top}) · {data.Recipes.Count} recipes";
            HasSkills = Skills.Count > 0;
            ShowEmptyState = !HasSkills;
        }
        catch (Exception ex)
        {
            _context.Host.Log.Warn($"inspector could not read player save: {ex.Message}");
            Summary = "Could not read this player save.";
            HasSkills = false;
            ShowEmptyState = true;
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One row in the skills list. A record so XAML compiled bindings have a data type.</summary>
public sealed record SkillRow(string Name, int Level, float Xp)
{
    public string LevelText => $"Lv {Level}";

    public string XpText => $"{Xp.ToString("N0", CultureInfo.InvariantCulture)} XP";
}
