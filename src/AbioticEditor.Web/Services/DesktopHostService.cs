using System.Diagnostics;
using AbioticEditor.Ui;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Local-desktop implementation of the host services used by Razor components.
/// It deliberately has no native UI-framework dependency: a self-hosted Razor build can select
/// files and folders, reveal a save, and open web links on Windows and Linux.
/// </summary>
/// <remarks>
/// This service runs on the machine that hosts the Razor application. A remotely
/// hosted web site cannot access a visitor's file system; those deployments should
/// keep using the manual path entry and ordinary browser links instead.
/// </remarks>
public sealed class DesktopHostService : IFilePicker, IFolderPicker, IExternalNavigationService
{
    private readonly IDesktopProcessRunner _processRunner;

    public DesktopHostService(IDesktopProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DesktopProcessRunner();
    }

    public async Task<PickedFile?> PickFileAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        var files = await PickFilesAsync(request, cancellationToken);
        return files.Count == 0 ? null : files[0];
    }

    public async Task<IReadOnlyList<PickedFile>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var paths = await WindowsDesktopPicker.PickFilesAsync(request.Title, allowMultiple: true, request.FileTypes);
        return paths.Where(File.Exists)
            .Select(path => new PickedFile(Path.GetFileName(path), path,
                token => Task.FromResult<Stream>(File.OpenRead(path))))
            .ToArray();
#else
        DesktopProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(DesktopHostCommandFactory.CreateFilePickerCommand(
                DesktopHostCommandFactory.Current, request, allowMultiple: true), cancellationToken);
        }
        catch (Exception exception) when (IsDesktopCommandUnavailable(exception))
        {
            throw new InvalidOperationException($"The local file picker is unavailable. {ManualPathEntryGuidance}", exception);
        }
        if (result.ExitCode != 0) return Array.Empty<PickedFile>();

        return result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(File.Exists)
            .Select(path => new PickedFile(Path.GetFileName(path), path,
                token => Task.FromResult<Stream>(File.OpenRead(path))))
            .ToArray();
#endif
    }

    public async Task<PickedFolder?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default)
    {
#if WINDOWS
        var path = await WindowsDesktopPicker.PickFolderAsync(request.Title);
        return path is not null && Directory.Exists(path)
            ? new PickedFolder(Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), path)
            : null;
#else
        DesktopProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(DesktopHostCommandFactory.CreateFolderPickerCommand(
                DesktopHostCommandFactory.Current, request), cancellationToken);
        }
        catch (Exception exception) when (IsDesktopCommandUnavailable(exception))
        {
            throw new InvalidOperationException($"The local folder picker is unavailable. {ManualPathEntryGuidance}", exception);
        }
        if (result.ExitCode != 0) return null;

        var pathResult = result.StandardOutput.Trim();
        return Directory.Exists(pathResult) ? new PickedFolder(Path.GetFileName(pathResult.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), pathResult) : null;
#endif
    }

    public async Task OpenUrlAsync(Uri url, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (!url.IsAbsoluteUri || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Only absolute HTTP and HTTPS URLs can be opened.", nameof(url));

        var result = await _processRunner.RunAsync(DesktopHostCommandFactory.CreateOpenUrlCommand(
            DesktopHostCommandFactory.Current, url), cancellationToken);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("The operating system could not open the requested link.");
    }

    public async Task RevealPathAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException("The file or folder to reveal does not exist.", fullPath);

        var command = DesktopHostCommandFactory.CreateRevealCommand(
            DesktopHostCommandFactory.Current, fullPath, File.Exists(fullPath));
        DesktopProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsDesktopCommandUnavailable(exception))
        {
            throw new InvalidOperationException($"The local file manager could not be started. Open this path manually: {fullPath}", exception);
        }
        // Windows explorer.exe hands the request to the running shell and then exits with
        // code 1 even when the window opens fine, so its exit code carries no failure signal.
        if (result.ExitCode != 0 && !IsExitCodeMeaningless(command))
            throw new InvalidOperationException($"The operating system could not reveal the selected path. Open this path manually: {fullPath}");
    }

    /// <summary>Fallback used by Razor pages when a browser or minimal Linux install cannot show a native picker.</summary>
    public const string ManualPathEntryGuidance = "Paste the absolute folder path into the path field instead.";

    private static bool IsDesktopCommandUnavailable(Exception exception)
        => exception is System.ComponentModel.Win32Exception or FileNotFoundException or DirectoryNotFoundException;

    /// <summary>Explorer.exe forwards to the running shell and always exits 1; its exit code says nothing.</summary>
    private static bool IsExitCodeMeaningless(DesktopProcessCommand command)
        => string.Equals(command.FileName, "explorer.exe", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Process boundary used by <see cref="DesktopHostService"/> and its tests.</summary>
public interface IDesktopProcessRunner
{
    Task<DesktopProcessResult> RunAsync(DesktopProcessCommand command, CancellationToken cancellationToken = default);
}

public sealed record DesktopProcessCommand(string FileName, IReadOnlyList<string> Arguments);
public sealed record DesktopProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class DesktopProcessRunner : IDesktopProcessRunner
{
    public async Task<DesktopProcessResult> RunAsync(DesktopProcessCommand command, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(command.FileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in command.Arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {command.FileName}.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new DesktopProcessResult(process.ExitCode, await output, await error);
    }
}

public enum DesktopHostPlatform { Windows, Linux, MacOS }
public enum LinuxDialogBackend { Zenity, KDialog }

/// <summary>Creates argument-list based commands so paths with spaces remain safe on every host.</summary>
public static class DesktopHostCommandFactory
{
    public static DesktopHostPlatform Current => OperatingSystem.IsWindows() ? DesktopHostPlatform.Windows
        : OperatingSystem.IsMacOS() ? DesktopHostPlatform.MacOS : DesktopHostPlatform.Linux;

    public static DesktopProcessCommand CreateFolderPickerCommand(DesktopHostPlatform platform, FolderPickerRequest request,
        LinuxDialogBackend? linuxDialogBackend = null) => platform switch
    {
        DesktopHostPlatform.Windows => new("powershell.exe", ["-NoProfile", "-STA", "-Command",
            WindowsDialogOwnerScript
            + "$d=New-Object System.Windows.Forms.FolderBrowserDialog;$d.Description='" + EscapePowerShell(request.Title ?? "Choose save folder")
            + "';try{if($d.ShowDialog($o) -eq 'OK'){[Console]::Write($d.SelectedPath)}}finally{$d.Dispose();$o.Close();$o.Dispose()}"]),
        DesktopHostPlatform.MacOS => new("osascript", ["-e", "POSIX path of (choose folder with prompt \"" + EscapeAppleScript(request.Title ?? "Choose save folder") + "\")"]),
        _ => CreateLinuxFolderPickerCommand(request, linuxDialogBackend ?? ResolveLinuxDialogBackend()),
    };

    public static DesktopProcessCommand CreateFilePickerCommand(DesktopHostPlatform platform, FilePickerRequest request, bool allowMultiple,
        LinuxDialogBackend? linuxDialogBackend = null) => platform switch
    {
        DesktopHostPlatform.Windows => new("powershell.exe", ["-NoProfile", "-STA", "-Command",
            WindowsDialogOwnerScript
            + "$d=New-Object System.Windows.Forms.OpenFileDialog;$d.Title='" + EscapePowerShell(request.Title ?? "Choose file")
            + "';$d.Multiselect=" + (allowMultiple ? "$true" : "$false")
            + ";try{if($d.ShowDialog($o) -eq 'OK'){[Console]::Write(($d.FileNames -join [Environment]::NewLine))}}finally{$d.Dispose();$o.Close();$o.Dispose()}"]),
        DesktopHostPlatform.MacOS => new("osascript", ["-e", "POSIX path of (choose file with prompt \"" + EscapeAppleScript(request.Title ?? "Choose file") + "\")"]),
        _ => CreateLinuxFilePickerCommand(request, allowMultiple, linuxDialogBackend ?? ResolveLinuxDialogBackend()),
    };

    public static LinuxDialogBackend ResolveLinuxDialogBackend(Func<string, bool>? commandExists = null)
    {
        commandExists ??= IsCommandAvailable;
        if (commandExists("zenity")) return LinuxDialogBackend.Zenity;
        if (commandExists("kdialog")) return LinuxDialogBackend.KDialog;
        // The launcher preflight produces the actionable missing-dependency error. Keeping this
        // fallback deterministic also makes direct/development launches fail with the familiar name.
        return LinuxDialogBackend.Zenity;
    }

    private static DesktopProcessCommand CreateLinuxFolderPickerCommand(FolderPickerRequest request, LinuxDialogBackend backend)
        => backend == LinuxDialogBackend.KDialog
            ? new("kdialog", ["--getexistingdirectory", "", request.Title ?? "Choose save folder"])
            : new("zenity", ["--file-selection", "--directory", "--title=" + (request.Title ?? "Choose save folder")]);

    private static DesktopProcessCommand CreateLinuxFilePickerCommand(FilePickerRequest request, bool allowMultiple, LinuxDialogBackend backend)
    {
        if (backend == LinuxDialogBackend.Zenity) return new("zenity", BuildZenityFileArguments(request, allowMultiple));

        var arguments = new List<string> { "--getopenfilename", "" };
        var filters = request.FileTypes.Select(type =>
        {
            var patterns = string.Join(' ', type.Extensions.Select(extension => "*" + (extension.StartsWith('.') ? extension : "." + extension)));
            return string.IsNullOrWhiteSpace(patterns) ? string.Empty : $"{patterns}|{type.Name}";
        }).Where(filter => filter.Length > 0);
        arguments.Add(string.Join('\n', filters));
        arguments.Add(request.Title ?? "Choose file");
        if (allowMultiple) { arguments.Add("--multiple"); arguments.Add("--separate-output"); }
        return new("kdialog", arguments);
    }

    // Windows must NOT use explorer.exe here: give it a URL with a query string
    // ("...?goto=...&x=y") and it opens a File Explorer window at Documents instead of the
    // browser, and it exits with code 1 either way. The url.dll FileProtocolHandler hands
    // the full URL (query string included) to the default browser and exits 0 on success.
    public static DesktopProcessCommand CreateOpenUrlCommand(DesktopHostPlatform platform, Uri url) => platform switch
    {
        DesktopHostPlatform.Windows => new("rundll32.exe", ["url.dll,FileProtocolHandler", url.AbsoluteUri]),
        DesktopHostPlatform.MacOS => new("open", [url.AbsoluteUri]),
        _ => new("xdg-open", [url.AbsoluteUri]),
    };

    public static DesktopProcessCommand CreateRevealCommand(DesktopHostPlatform platform, string fullPath, bool isFile) => platform switch
    {
        DesktopHostPlatform.Windows => isFile
            ? new("explorer.exe", ["/select," + fullPath])
            : new("explorer.exe", [fullPath]),
        DesktopHostPlatform.MacOS => isFile
            ? new("open", ["-R", fullPath])
            : new("open", [fullPath]),
        _ => new("xdg-open", [isFile ? DirectoryNameFor(DesktopHostPlatform.Linux, fullPath) : fullPath]),
    };

    private static List<string> BuildZenityFileArguments(FilePickerRequest request, bool allowMultiple)
    {
        var arguments = new List<string> { "--file-selection", "--title=" + (request.Title ?? "Choose file") };
        if (allowMultiple) { arguments.Add("--multiple"); arguments.Add("--separator=\n"); }
        foreach (var type in request.FileTypes)
        {
            var patterns = string.Join(' ', type.Extensions.Select(extension => "*" + (extension.StartsWith('.') ? extension : "." + extension)));
            if (!string.IsNullOrWhiteSpace(patterns)) arguments.Add("--file-filter=" + type.Name + " | " + patterns);
        }
        return arguments;
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    // A picker launched from the desktop web view otherwise has no native owner and can
    // appear behind the editor, which looks exactly like a dead button. The invisible,
    // topmost owner keeps the operating-system dialog in front without adding taskbar chrome.
    private const string WindowsDialogOwnerScript =
        "Add-Type -AssemblyName System.Windows.Forms;$o=New-Object System.Windows.Forms.Form;"
        + "$o.ShowInTaskbar=$false;$o.TopMost=$true;$o.Opacity=0;$o.Width=1;$o.Height=1;"
        + "$o.StartPosition='CenterScreen';$o.Show();";
    private static string EscapeAppleScript(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string DirectoryNameFor(DesktopHostPlatform platform, string path)
    {
        var separator = platform == DesktopHostPlatform.Windows ? '\\' : '/';
        var normalised = platform == DesktopHostPlatform.Windows ? path.Replace('/', '\\') : path.Replace('\\', '/');
        var index = normalised.LastIndexOf(separator);
        return index > 0 ? normalised[..index] : normalised;
    }

    private static bool IsCommandAvailable(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(directory => File.Exists(Path.Combine(directory, command)));
    }
}
