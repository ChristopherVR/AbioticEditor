using AbioticEditor.Web.Services;
using Microsoft.Extensions.Logging;

namespace AbioticEditor.Tests;

public sealed class UserFacingErrorServiceTests
{
    [Fact]
    public void Present_logs_the_technical_exception_without_showing_it_to_the_player()
    {
        var logger = new CapturingLogger<UserFacingErrorService>();
        var service = new UserFacingErrorService(logger);
        var exception = new IOException("C:\\private\\save.sav was locked by process 4242");

        var message = service.Present(
            exception,
            "The save could not be written.",
            "Check folder permissions and try again.");

        Assert.Equal("The save could not be written. Check folder permissions and try again.", message);
        Assert.DoesNotContain("private", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("4242", message, StringComparison.Ordinal);
        Assert.Same(exception, logger.Exception);
        Assert.Equal(LogLevel.Error, logger.Level);
        Assert.Contains("save could not be written", logger.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Razor_components_never_render_exception_messages_directly()
    {
        var root = FindRepositoryRoot();
        var components = Path.Combine(root, "src", "AbioticEditor.Web", "Components");
        var offenders = Directory.EnumerateFiles(components, "*.razor", SearchOption.AllDirectories)
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("ex.Message", StringComparison.Ordinal)
                    || source.Contains("exception.Message", StringComparison.Ordinal)
                    || source.Contains("Exception.Message", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(root, path))
            .ToArray();

        Assert.True(offenders.Length == 0, $"Raw exception details are player-visible in: {string.Join(", ", offenders)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "AbioticEditor.Web")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public LogLevel Level { get; private set; }
        public Exception? Exception { get; private set; }
        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Level = logLevel;
            Exception = exception;
            Message = formatter(state, exception);
        }
    }
}
