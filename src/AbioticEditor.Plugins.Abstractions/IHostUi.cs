namespace AbioticEditor.Plugins;

/// <summary>
/// The host application's UI surface, exposed to plugins so they can drive the app: show
/// dialogs/toasts, run a registered save operation against the open save, reload it, or open
/// app screens. This is the "let plugin code interact with the app UI" channel.
///
/// <para>
/// It is host-agnostic by design: the GUI implements it against its view-models and dialogs,
/// the CLI (and tests) get <see cref="NullHostUi"/> which no-ops. All methods are async and
/// the GUI marshals them onto the UI thread, so a plugin can call them from any thread (and,
/// for JavaScript plugins, fire-and-forget via the <c>abiotic.ui</c> wrapper).
/// </para>
/// </summary>
public interface IHostUi
{
    /// <summary>Absolute path of the save the app currently has open, or null.</summary>
    string? OpenSavePath { get; }

    /// <summary>Shows an informational dialog with an OK button.</summary>
    Task ShowAlertAsync(string title, string message);

    /// <summary>Shows a yes/no dialog; resolves true if the user confirmed.</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Shows a transient toast/snackbar message.</summary>
    Task ToastAsync(string message);

    /// <summary>
    /// Runs a registered save operation (by id) against the open save through the host's
    /// backup/write path, then reloads the editor. Returns true if it ran and wrote. A no-op
    /// returning false when no save is open or no such operation exists.
    /// </summary>
    Task<bool> RunSaveOperationAsync(string operationId, IReadOnlyDictionary<string, string>? parameters = null);

    /// <summary>Re-reads the open save from disk and rebuilds its editor.</summary>
    Task ReloadOpenSaveAsync();

    /// <summary>Opens the Settings screen.</summary>
    Task OpenSettingsAsync();

    /// <summary>Opens the Plugins management screen.</summary>
    Task OpenPluginsPanelAsync();
}

/// <summary>
/// Default <see cref="IHostUi"/> used by hosts without a UI (the CLI, tests). Every method is
/// a no-op, so plugin code that calls the UI bridge is safe everywhere and simply does
/// nothing where there is no app to drive.
/// </summary>
public sealed class NullHostUi : IHostUi
{
    /// <summary>The shared no-op instance.</summary>
    public static readonly NullHostUi Instance = new();

    private NullHostUi()
    {
    }

    /// <inheritdoc/>
    public string? OpenSavePath => null;

    /// <inheritdoc/>
    public Task ShowAlertAsync(string title, string message) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task ToastAsync(string message) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<bool> RunSaveOperationAsync(string operationId, IReadOnlyDictionary<string, string>? parameters = null)
        => Task.FromResult(false);

    /// <inheritdoc/>
    public Task ReloadOpenSaveAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task OpenSettingsAsync() => Task.CompletedTask;

    /// <inheritdoc/>
    public Task OpenPluginsPanelAsync() => Task.CompletedTask;
}
