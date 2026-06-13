using System.ComponentModel;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// One step of the linear story timeline on the flags tab: a chapter and whether its
/// trigger flag is present in this save. Checking the box sets the flag; unchecking
/// removes it.
/// </summary>
public sealed class StoryFlagItemViewModel : INotifyPropertyChanged
{
    private readonly WorldEditorViewModel _owner;

    public StoryFlagItemViewModel(WorldEditorViewModel owner, StoryChapter chapter, int number)
    {
        _owner = owner;
        Chapter = chapter;
        Number = number;
    }

    public StoryChapter Chapter { get; }
    public int Number { get; }

    public string NumberText => $"{Number:D2}";
    public string Title => Chapter.Title;
    public string FlagName => Chapter.TriggerFlag ?? string.Empty;
    public string? Summary => Chapter.Summary;
    public bool HasSummary => !string.IsNullOrEmpty(Chapter.Summary);

    public bool IsSet
    {
        get => Chapter.TriggerFlag is not null
               && _owner.Flags.Contains(Chapter.TriggerFlag, StringComparer.OrdinalIgnoreCase);
        set
        {
            if (Chapter.TriggerFlag is null || IsSet == value) return;
            if (value)
            {
                _owner.Flags.Add(Chapter.TriggerFlag);
            }
            else
            {
                var match = _owner.Flags.FirstOrDefault(f =>
                    string.Equals(f, Chapter.TriggerFlag, StringComparison.OrdinalIgnoreCase));
                if (match is not null) _owner.Flags.Remove(match);
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSet)));
        }
    }

    public void NotifyChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSet)));

    public event PropertyChangedEventHandler? PropertyChanged;
}
