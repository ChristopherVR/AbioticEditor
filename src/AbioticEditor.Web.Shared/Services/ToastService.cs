namespace AbioticEditor.Web.Services;

public enum ToastKind { Information, Success, Warning, Error }
public sealed record ToastMessage(Guid Id, string Text, ToastKind Kind);

/// <summary>Provides circuit-local status feedback.</summary>
public sealed class ToastService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);
    private readonly List<ToastMessage> _messages = [];
    private readonly HashSet<Guid> _paused = [];
    public IReadOnlyList<ToastMessage> Messages => _messages;
    public event Action? Changed;

    public void Show(string text, ToastKind kind = ToastKind.Information, TimeSpan? duration = null)
    {
        var message = new ToastMessage(Guid.NewGuid(), text, kind);
        _messages.Add(message);
        Changed?.Invoke();
        _ = RemoveAfterAsync(message.Id, duration ?? DefaultDuration(kind));
    }

    public void Dismiss(Guid id)
    {
        _paused.Remove(id);
        if (_messages.RemoveAll(message => message.Id == id) > 0) Changed?.Invoke();
    }

    /// <summary>Freezes a toast's auto-dismiss countdown while the pointer hovers it.</summary>
    public void Pause(Guid id) => _paused.Add(id);

    /// <summary>Resumes the auto-dismiss countdown when the pointer leaves the toast.</summary>
    public void Resume(Guid id) => _paused.Remove(id);

    // Warnings and errors stay up long enough to actually be read.
    private static TimeSpan DefaultDuration(ToastKind kind) => kind switch
    {
        ToastKind.Error => TimeSpan.FromSeconds(12),
        ToastKind.Warning => TimeSpan.FromSeconds(9),
        _ => TimeSpan.FromSeconds(5),
    };

    private async Task RemoveAfterAsync(Guid id, TimeSpan duration)
    {
        var remaining = duration;
        while (remaining > TimeSpan.Zero)
        {
            await Task.Delay(Tick);
            if (_messages.All(message => message.Id != id)) return; // dismissed by hand
            if (_paused.Contains(id)) continue; // hovered: hold the countdown
            remaining -= Tick;
        }
        Dismiss(id);
    }
}
