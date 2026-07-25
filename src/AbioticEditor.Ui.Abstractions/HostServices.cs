namespace AbioticEditor.Ui;

/// <summary>Displays transient, non-modal application messages.</summary>
public interface INotificationService
{
    /// <summary>Shows a notification using the host's appropriate presentation.</summary>
    Task NotifyAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>A transient message for the user.</summary>
public sealed record NotificationRequest(string Message, NotificationSeverity Severity = NotificationSeverity.Information, string? Title = null);

/// <summary>The urgency associated with a notification.</summary>
public enum NotificationSeverity
{
    /// <summary>A routine informational message.</summary>
    Information,

    /// <summary>A successful operation.</summary>
    Success,

    /// <summary>A condition needing attention but not preventing work.</summary>
    Warning,

    /// <summary>An operation failure.</summary>
    Error,
}

/// <summary>Performs actions outside the application surface.</summary>
public interface IExternalNavigationService
{
    /// <summary>Opens an absolute URL using the host's default handler.</summary>
    Task OpenUrlAsync(Uri url, CancellationToken cancellationToken = default);

    /// <summary>Reveals a local file or directory in the host's file manager.</summary>
    Task RevealPathAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Dispatches work to the UI context when a host requires one.</summary>
public interface IUiDispatcher
{
    /// <summary>Gets whether work submitted from the current context must be dispatched.</summary>
    bool IsDispatchRequired { get; }

    /// <summary>Runs synchronous work on the UI context.</summary>
    Task InvokeAsync(Action action, CancellationToken cancellationToken = default);

    /// <summary>Runs asynchronous work on the UI context.</summary>
    Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default);

    /// <summary>Runs synchronous work returning a value on the UI context.</summary>
    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

    /// <summary>Runs asynchronous work returning a value on the UI context.</summary>
    Task<T> InvokeAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken = default);
}
