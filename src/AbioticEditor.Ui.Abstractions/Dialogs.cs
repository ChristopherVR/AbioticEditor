namespace AbioticEditor.Ui;

/// <summary>Displays application-owned modal dialogs.</summary>
public interface IDialogService
{
    /// <summary>Shows a dialog and completes after the user dismisses it.</summary>
    Task<DialogResult> ShowAsync(DialogRequest request, CancellationToken cancellationToken = default);
}

/// <summary>The content and actions of an application-owned dialog.</summary>
public sealed record DialogRequest
{
    /// <summary>Dialog heading.</summary>
    public required string Title { get; init; }

    /// <summary>Primary dialog text.</summary>
    public required string Message { get; init; }

    /// <summary>Actions presented to the user, in display order.</summary>
    public IReadOnlyList<DialogAction> Actions { get; init; } = Array.Empty<DialogAction>();

    /// <summary>Optional one-line text input to render in the dialog.</summary>
    public DialogTextInput? TextInput { get; init; }

    /// <summary>Whether clicking outside the dialog may dismiss it.</summary>
    public bool IsDismissible { get; init; } = true;
}

/// <summary>An action a user may choose in a dialog.</summary>
public sealed record DialogAction(string Id, string Text, DialogActionTone Tone = DialogActionTone.Neutral, bool IsDefault = false);

/// <summary>Visual intent of a dialog action.</summary>
public enum DialogActionTone
{
    /// <summary>A non-destructive, secondary action.</summary>
    Neutral,

    /// <summary>The primary affirmative action.</summary>
    Primary,

    /// <summary>An irreversible or potentially destructive action.</summary>
    Danger,
}

/// <summary>Configuration for a single-line dialog text input.</summary>
public sealed record DialogTextInput
{
    /// <summary>Accessible label displayed for the input.</summary>
    public required string Label { get; init; }

    /// <summary>Value initially shown in the input.</summary>
    public string InitialValue { get; init; } = string.Empty;

    /// <summary>Optional hint shown when the input is empty.</summary>
    public string? Placeholder { get; init; }

    /// <summary>Maximum permitted input length, or <see langword="null"/> for no host-imposed limit.</summary>
    public int? MaxLength { get; init; }
}

/// <summary>The user's dialog choice and optional entered text.</summary>
public sealed record DialogResult(string? ActionId, string? Text = null)
{
    /// <summary>Gets whether the dialog was dismissed without selecting an action.</summary>
    public bool IsDismissed => ActionId is null;
}
