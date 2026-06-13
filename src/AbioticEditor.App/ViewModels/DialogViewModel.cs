using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace AbioticEditor.App.ViewModels;

/// <summary>Visual weight of a dialog button.</summary>
public enum DialogTone
{
    /// <summary>The default / affirmative action (accent fill).</summary>
    Primary,

    /// <summary>A destructive or irreversible action (red fill).</summary>
    Danger,

    /// <summary>A dismissive / secondary action (muted fill).</summary>
    Neutral,
}

/// <summary>One button in an in-app dialog.</summary>
public sealed class DialogActionViewModel
{
    public DialogActionViewModel(string text, DialogTone tone, Action invoke)
    {
        Text = text;
        Tone = tone;
        Command = new RelayCommand(invoke);
        BackgroundColor = Res(tone switch
        {
            DialogTone.Primary => "AfAccentOrange",
            DialogTone.Danger => "AfAlertRed",
            _ => "AfPanelElevated",
        });
        TextColor = Res(tone switch
        {
            DialogTone.Primary => "AfTextOnAccent",
            DialogTone.Danger => "AfTextOnAccent",
            _ => "AfTextPrimary",
        });
    }

    public string Text { get; }
    public DialogTone Tone { get; }
    public ICommand Command { get; }

    /// <summary>Resolved at creation from the current theme - dialogs are transient.</summary>
    public Color BackgroundColor { get; }
    public Color TextColor { get; }

    private static Color Res(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Gray;
}

/// <summary>
/// App-owned modal dialog state. One shared instance (<see cref="Current"/>) is bound by
/// the always-present <c>DialogHostView</c> overlay; any caller - view code-behind, a
/// view-model, or a static helper - awaits <see cref="ShowAsync"/> / <see cref="ConfirmAsync"/>
/// without needing a page reference. Replaces the platform <c>DisplayAlert</c> /
/// <c>DisplayActionSheet</c> popups with a styled, animated in-app card.
/// </summary>
public sealed class DialogViewModel : INotifyPropertyChanged
{
    /// <summary>The single instance the overlay binds to and helpers raise.</summary>
    public static DialogViewModel Current { get; } = new();

    private bool _isOpen;
    private string _title = string.Empty;
    private string _message = string.Empty;
    private IReadOnlyList<DialogActionViewModel> _actions = Array.Empty<DialogActionViewModel>();
    private TaskCompletionSource<int>? _pending;
    private bool _showInput;
    private string _inputText = string.Empty;
    private string _inputPlaceholder = string.Empty;

    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    public bool HasTitle => _title.Length > 0;

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public IReadOnlyList<DialogActionViewModel> Actions
    {
        get => _actions;
        private set => Set(ref _actions, value);
    }

    /// <summary>True while the dialog is collecting a line of text (see <see cref="PromptAsync"/>).</summary>
    public bool ShowInput
    {
        get => _showInput;
        private set => Set(ref _showInput, value);
    }

    /// <summary>The current contents of the input field (two-way bound by the host).</summary>
    public string InputText
    {
        get => _inputText;
        set => Set(ref _inputText, value);
    }

    /// <summary>Placeholder shown in the input field while empty.</summary>
    public string InputPlaceholder
    {
        get => _inputPlaceholder;
        private set => Set(ref _inputPlaceholder, value);
    }

    /// <summary>
    /// Shows the dialog and resolves with the index of the chosen action (left-to-right),
    /// or -1 if it was dismissed by tapping the scrim. Must be awaited on the UI thread
    /// (every caller is an async UI handler). Opening a second dialog cancels the first.
    /// </summary>
    public Task<int> ShowAsync(string title, string message, params (string Text, DialogTone Tone)[] actions)
    {
        _pending?.TrySetResult(-1);

        ShowInput = false;
        Title = title;
        Message = message;
        Actions = actions
            .Select((a, i) => new DialogActionViewModel(a.Text, a.Tone, () => Complete(i)))
            .ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTitle)));

        var tcs = new TaskCompletionSource<int>();
        _pending = tcs;
        IsOpen = true;
        return tcs.Task;
    }

    /// <summary>Two-button confirm. Returns true when the accept action was chosen.</summary>
    public async Task<bool> ConfirmAsync(
        string title, string message, string accept, string cancel, DialogTone acceptTone = DialogTone.Primary)
    {
        // Cancel sits left (index 0), accept right (index 1) - the affirmative reads last.
        var choice = await ShowAsync(title, message, (cancel, DialogTone.Neutral), (accept, acceptTone));
        return choice == 1;
    }

    /// <summary>Single-button informational dialog.</summary>
    public Task AlertAsync(string title, string message, string ok = "OK")
        => ShowAsync(title, message, (ok, DialogTone.Primary));

    /// <summary>
    /// Shows the dialog with a single-line text field and returns the entered text when the
    /// accept button is chosen, or null when cancelled / dismissed. The field starts at
    /// <paramref name="initialValue"/>.
    /// </summary>
    public async Task<string?> PromptAsync(
        string title, string message, string placeholder = "",
        string initialValue = "", string accept = "OK", string cancel = "Cancel")
    {
        InputText = initialValue ?? string.Empty;
        InputPlaceholder = placeholder ?? string.Empty;

        // ShowAsync resets ShowInput to false synchronously, so flip it on afterwards.
        var task = ShowAsync(title, message, (cancel, DialogTone.Neutral), (accept, DialogTone.Primary));
        ShowInput = true;

        var choice = await task;
        ShowInput = false;
        return choice == 1 ? InputText : null;
    }

    /// <summary>Scrim tap = dismiss as "no choice" (cancel).</summary>
    public void DismissViaScrim() => Complete(-1);

    private void Complete(int index)
    {
        IsOpen = false;
        var tcs = _pending;
        _pending = null;
        tcs?.TrySetResult(index);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
