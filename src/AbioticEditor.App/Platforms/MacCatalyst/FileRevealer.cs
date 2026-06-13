using System.Diagnostics;

namespace AbioticEditor.App.Views;

/// <summary>macOS: reveal the file in Finder (<c>open -R "path"</c>).</summary>
public static partial class FileRevealer
{
    static partial void PlatformReveal(string path)
    {
        // -R reveals (selects) the file in Finder rather than opening it. Pass the path
        // via ArgumentList (not a single argument string) so a path that starts with '-'
        // or contains quotes can't smuggle extra flags into `open` (argument injection).
        // The "--" terminator guarantees the path is treated as an operand, not an option.
        Process.Start(new ProcessStartInfo("open")
        {
            UseShellExecute = false,
            ArgumentList = { "-R", "--", path },
        });
    }
}
