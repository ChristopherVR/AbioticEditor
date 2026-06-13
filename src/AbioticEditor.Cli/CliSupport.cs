using System.Text.Json;

namespace AbioticEditor.Cli;

/// <summary>
/// A problem caused by the user's input (missing file, bad id, refused operation).
/// Reported on stderr and mapped to exit code 1; everything else unexpected is 2.
/// </summary>
public sealed class CliUserErrorException : Exception
{
    public CliUserErrorException()
    {
    }

    public CliUserErrorException(string message)
        : base(message)
    {
    }

    public CliUserErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Exit-code policy and small output helpers shared by every command.</summary>
internal static class Cli
{
    public const int Ok = 0;
    public const int UserError = 1;
    public const int UnexpectedError = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Runs a command body under the exit-code contract: 0 ok, 1 user error
    /// (bad arguments, missing/unparseable files, refused writes), 2 unexpected.
    /// </summary>
    public static int Run(Func<int> body)
    {
        try
        {
            return body();
        }
        catch (CliUserErrorException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return UserError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or InvalidDataException or FormatException or NotSupportedException)
        {
            // Bad or inaccessible input files are the user's domain, not a crash.
            Console.Error.WriteLine($"error: {ex.Message}");
            return UserError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"unexpected error: {ex}");
            return UnexpectedError;
        }
    }

    /// <summary>Asserts a required input file exists; returns its full path.</summary>
    public static string RequireFile(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliUserErrorException($"missing {what} path.");
        }
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new CliUserErrorException($"{what} not found: {full}");
        }
        return full;
    }

    /// <summary>Asserts a required directory exists; returns its full path.</summary>
    public static string RequireDirectory(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CliUserErrorException($"missing {what} path.");
        }
        var full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
        {
            throw new CliUserErrorException($"{what} not found: {full}");
        }
        return full;
    }

    /// <summary>Serializes a payload as indented JSON on stdout (machine output).</summary>
    public static void WriteJson(object payload)
        => Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    /// <summary>Informational stdout line, suppressed by <c>--quiet</c>.</summary>
    public static void Info(bool quiet, string message)
    {
        if (!quiet)
        {
            Console.WriteLine(message);
        }
    }

    /// <summary>Warning on stderr (never suppressed - warnings are part of the contract).</summary>
    public static void Warn(string message)
        => Console.Error.WriteLine($"warning: {message}");
}
