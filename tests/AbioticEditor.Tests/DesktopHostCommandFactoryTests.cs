using AbioticEditor.Ui;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class DesktopHostCommandFactoryTests
{
    [Fact]
    public void Linux_reveal_of_file_opens_its_parent_directory()
    {
        var command = DesktopHostCommandFactory.CreateRevealCommand(DesktopHostPlatform.Linux, "/home/test/save folder/Player_1.sav", isFile: true);

        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal("/home/test/save folder", Assert.Single(command.Arguments));
    }

    [Fact]
    public void Windows_reveal_of_file_requests_file_selection()
    {
        var command = DesktopHostCommandFactory.CreateRevealCommand(DesktopHostPlatform.Windows, @"C:\Saves\Player_1.sav", isFile: true);

        Assert.Equal("explorer.exe", command.FileName);
        Assert.Equal(@"/select,C:\Saves\Player_1.sav", Assert.Single(command.Arguments));
    }

    [Fact]
    public void Windows_open_url_hands_the_full_url_to_the_default_browser_handler()
    {
        // explorer.exe must not be used for URLs: with a query string it opens a File
        // Explorer window at Documents instead of the browser (and exits 1 either way).
        var url = new Uri("https://steamcommunity.com/login/home/?goto=profiles/76561197960265728/stats/427410/achievements");

        var command = DesktopHostCommandFactory.CreateOpenUrlCommand(DesktopHostPlatform.Windows, url);

        Assert.Equal("rundll32.exe", command.FileName);
        Assert.Equal(["url.dll,FileProtocolHandler", url.AbsoluteUri], command.Arguments);
    }

    [Fact]
    public void Linux_open_url_uses_xdg_open()
    {
        var url = new Uri("https://steamcommunity.com/my/edit/settings");

        var command = DesktopHostCommandFactory.CreateOpenUrlCommand(DesktopHostPlatform.Linux, url);

        Assert.Equal("xdg-open", command.FileName);
        Assert.Equal(url.AbsoluteUri, Assert.Single(command.Arguments));
    }

    [Fact]
    public async Task Reveal_tolerates_the_windows_shell_exit_code()
    {
        // explorer.exe reports exit code 1 even when the reveal window opens fine, so a
        // successful export must not be reported as a failure because of it.
        if (!OperatingSystem.IsWindows()) return;
        var runner = new RecordingRunner(exitCode: 1);
        var service = new DesktopHostService(runner);
        var file = Path.Combine(Path.GetTempPath(), $"abiotic-reveal-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(file, "{}");
        try
        {
            await service.RevealPathAsync(file);

            var command = Assert.Single(runner.Commands);
            Assert.Equal("explorer.exe", command.FileName);
        }
        finally { File.Delete(file); }
    }

    private sealed class RecordingRunner(int exitCode) : IDesktopProcessRunner
    {
        public List<DesktopProcessCommand> Commands { get; } = [];

        public Task<DesktopProcessResult> RunAsync(DesktopProcessCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(new DesktopProcessResult(exitCode, string.Empty, string.Empty));
        }
    }

    [Fact]
    public void Windows_folder_picker_owns_a_foreground_dialog()
    {
        var command = DesktopHostCommandFactory.CreateFolderPickerCommand(DesktopHostPlatform.Windows,
            new FolderPickerRequest { Title = "Choose world's saves" });

        Assert.Equal("powershell.exe", command.FileName);
        Assert.Contains("-STA", command.Arguments);
        var script = command.Arguments[^1];
        Assert.Contains("$o.TopMost=$true", script, StringComparison.Ordinal);
        Assert.Contains("ShowDialog($o)", script, StringComparison.Ordinal);
        Assert.Contains("Choose world''s saves", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_file_picker_owns_a_foreground_dialog()
    {
        var command = DesktopHostCommandFactory.CreateFilePickerCommand(DesktopHostPlatform.Windows,
            new FilePickerRequest { Title = "Choose saves" }, allowMultiple: true);

        Assert.Equal("powershell.exe", command.FileName);
        var script = command.Arguments[^1];
        Assert.Contains("$o.TopMost=$true", script, StringComparison.Ordinal);
        Assert.Contains("ShowDialog($o)", script, StringComparison.Ordinal);
        Assert.Contains("$d.Multiselect=$true", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Linux_file_picker_includes_requested_extensions()
    {
        var command = DesktopHostCommandFactory.CreateFilePickerCommand(DesktopHostPlatform.Linux,
            new FilePickerRequest { FileTypes = [new FileTypeFilter("Save files", ["sav", ".json"])] }, allowMultiple: true);

        Assert.Equal("zenity", command.FileName);
        Assert.Contains("--multiple", command.Arguments);
        Assert.Contains("--file-filter=Save files | *.sav *.json", command.Arguments);
    }

    [Fact]
    public void Linux_dialog_backend_falls_back_to_kdialog_when_zenity_is_missing()
    {
        var backend = DesktopHostCommandFactory.ResolveLinuxDialogBackend(command => command == "kdialog");

        Assert.Equal(LinuxDialogBackend.KDialog, backend);
    }

    [Fact]
    public void Linux_kdialog_file_picker_supports_multiple_files_and_requested_extensions()
    {
        var command = DesktopHostCommandFactory.CreateFilePickerCommand(DesktopHostPlatform.Linux,
            new FilePickerRequest { Title = "Choose saves", FileTypes = [new FileTypeFilter("Save files", ["sav", ".json"])] },
            allowMultiple: true, LinuxDialogBackend.KDialog);

        Assert.Equal("kdialog", command.FileName);
        Assert.Contains("*.sav *.json|Save files", command.Arguments);
        Assert.Contains("--multiple", command.Arguments);
        Assert.Contains("--separate-output", command.Arguments);
    }

    [Fact]
    public void Linux_kdialog_folder_picker_uses_existing_directory_mode()
    {
        var command = DesktopHostCommandFactory.CreateFolderPickerCommand(DesktopHostPlatform.Linux,
            new FolderPickerRequest { Title = "Choose world" }, LinuxDialogBackend.KDialog);

        Assert.Equal("kdialog", command.FileName);
        Assert.Contains("--getexistingdirectory", command.Arguments);
        Assert.Contains("Choose world", command.Arguments);
    }
}
