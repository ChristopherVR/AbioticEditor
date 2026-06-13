namespace AbioticEditor.Updater;

/// <summary>
/// Minimal logging seam so the updater stays dependency-free. The CLI bridges this to its
/// console output and the app to <c>EditorLog</c>; tests pass <see cref="Null"/>.
/// </summary>
public interface IUpdaterLog
{
    void Info(string message);

    void Warn(string message);

    void Error(string message, Exception? ex = null);

    /// <summary>A no-op log used when the caller does not care about diagnostics.</summary>
    static IUpdaterLog Null { get; } = new NullLog();

    /// <summary>Wraps three callbacks as an <see cref="IUpdaterLog"/>.</summary>
    static IUpdaterLog Delegating(Action<string> info, Action<string> warn, Action<string, Exception?> error)
        => new DelegatingLog(info, warn, error);

    private sealed class NullLog : IUpdaterLog
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? ex = null)
        {
        }
    }

    private sealed class DelegatingLog(
        Action<string> info,
        Action<string> warn,
        Action<string, Exception?> error) : IUpdaterLog
    {
        public void Info(string message) => info(message);

        public void Warn(string message) => warn(message);

        public void Error(string message, Exception? ex = null) => error(message, ex);
    }
}
