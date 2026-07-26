using AbioticEditor.Core.Diagnostics;

namespace AbioticEditor.Web.Diagnostics;

/// <summary>
/// Sends everything written through <see cref="ILogger"/> to the editor's own log file.
///
/// <para>Without this the desktop app records nothing. The only providers a default host
/// registers are Console and Debug, and the published executable is marked as a graphical
/// program so Windows never gives it a console - so every framework warning, every torn-down
/// Blazor circuit, and all of <c>UserFacingErrorService</c>'s error reports went nowhere at
/// all. The one durable channel was <see cref="EditorLog"/>, which nothing in the logging
/// pipeline was connected to.</para>
///
/// <para>Errors and worse are always written, because <see cref="EditorLog.Error"/> ignores the
/// diagnostics switch: a crash must still leave a trace for someone who never turned logging
/// on. Anything below that is only written when they did.</para>
/// </summary>
public sealed class EditorLogLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new EditorLogLogger(categoryName);

    public void Dispose()
    {
        // EditorLog opens and closes the file per line; there is nothing to flush or release.
    }

    private sealed class EditorLogLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        // Warning and below are only worth the write when diagnostics are on; Error and above
        // are written regardless, so they must always be considered enabled.
        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= LogLevel.Error || (EditorLog.Enabled && logLevel >= LogLevel.Information);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);
            switch (logLevel)
            {
                case LogLevel.Critical:
                case LogLevel.Error:
                    EditorLog.Error(category, message, exception);
                    break;
                case LogLevel.Warning:
                    EditorLog.Warn(category, message, exception);
                    break;
                default:
                    EditorLog.Info(category, message, exception);
                    break;
            }
        }
    }
}
