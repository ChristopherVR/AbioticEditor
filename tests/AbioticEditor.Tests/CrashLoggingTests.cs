using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Web.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

/// <summary>
/// The desktop app has no console, so anything written only to the console provider is lost.
/// These cover the bridge that makes <see cref="ILogger"/> output land in the editor's log file
/// instead, which is the only record a player can send us after something breaks.
/// </summary>
public class CrashLoggingTests : IDisposable
{
    private readonly string _directory;
    private readonly string _previousDirectory;
    private readonly bool _previousEnabled;

    public CrashLoggingTests()
    {
        _previousDirectory = EditorLog.LogDirectory;
        _previousEnabled = EditorLog.Enabled;
        _directory = Path.Combine(Path.GetTempPath(), "abiotic-crashlog-tests", Guid.NewGuid().ToString("N"));
        EditorLog.LogDirectory = _directory;
    }

    public void Dispose()
    {
        EditorLog.LogDirectory = _previousDirectory;
        EditorLog.Enabled = _previousEnabled;
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string ReadLog()
        => File.Exists(EditorLog.CurrentLogFilePath) ? File.ReadAllText(EditorLog.CurrentLogFilePath) : string.Empty;

    private static ILogger CreateLogger(string category)
    {
        using var provider = new EditorLogLoggerProvider();
        return provider.CreateLogger(category);
    }

    // The raw ILogger.Log call rather than the LogError/LogWarning helpers, which the analyzer
    // rejects in favour of pre-built delegates. This is what those helpers end up calling.
    private static void Emit(ILogger logger, LogLevel level, string message, Exception? exception = null)
        => logger.Log(level, new EventId(0), message, exception, static (state, _) => state);

    /// <summary>
    /// The reported failure mode: a player who never opened Settings still needs their crash on
    /// disk, so errors must ignore the diagnostics switch.
    /// </summary>
    [Fact]
    public void An_error_is_written_even_with_diagnostics_turned_off()
    {
        EditorLog.Enabled = false;

        Emit(CreateLogger("Save"), LogLevel.Error, "Saving the world failed",
            new InvalidOperationException("world unlock failed"));

        var log = ReadLog();
        Assert.Contains("Saving the world failed", log, StringComparison.Ordinal);
        Assert.Contains("world unlock failed", log, StringComparison.Ordinal);
        Assert.Contains("[ERROR]", log, StringComparison.Ordinal);
    }

    [Fact]
    public void A_critical_is_written_as_an_error()
    {
        EditorLog.Enabled = false;

        Emit(CreateLogger("Host"), LogLevel.Critical, "the host is going down");

        Assert.Contains("[ERROR]", ReadLog(), StringComparison.Ordinal);
    }

    /// <summary>Routine chatter stays out of the file unless the player asked for it.</summary>
    [Fact]
    public void Information_and_warnings_are_silent_until_diagnostics_are_on()
    {
        EditorLog.Enabled = false;

        var logger = CreateLogger("Host");
        Emit(logger, LogLevel.Information, "listening");
        Emit(logger, LogLevel.Warning, "icon missing");

        Assert.Equal(string.Empty, ReadLog());
    }

    [Fact]
    public void Information_and_warnings_are_written_once_diagnostics_are_on()
    {
        EditorLog.Enabled = true;

        var logger = CreateLogger("Host");
        Emit(logger, LogLevel.Information, "listening");
        Emit(logger, LogLevel.Warning, "icon missing");

        var log = ReadLog();
        Assert.Contains("[INFO ]", log, StringComparison.Ordinal);
        Assert.Contains("[WARN ]", log, StringComparison.Ordinal);
    }

    /// <summary>The category is what tells us which part of the editor failed.</summary>
    [Fact]
    public void The_logger_category_identifies_the_source()
    {
        EditorLog.Enabled = false;

        Emit(CreateLogger("AbioticEditor.Web.Services.UserFacingErrorService"), LogLevel.Error, "action failed");

        Assert.Contains("AbioticEditor.Web.Services.UserFacingErrorService", ReadLog(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Installing twice would double every crash entry, and the host and a test can both reach
    /// this call.
    /// </summary>
    [Fact]
    public void Installing_the_crash_handlers_more_than_once_is_harmless()
    {
        CrashLog.Install();
        CrashLog.Install();
    }
}
