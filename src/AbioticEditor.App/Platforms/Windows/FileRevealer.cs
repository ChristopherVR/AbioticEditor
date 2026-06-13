using System.Diagnostics;

namespace AbioticEditor.App.Views;

/// <summary>Windows: open Explorer with the file selected (<c>explorer /select,"path"</c>).</summary>
public static partial class FileRevealer
{
    static partial void PlatformReveal(string path)
    {
        // explorer.exe interprets the comma form as "open the containing folder and
        // highlight this item". Quote the path so spaces survive.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true,
        });
    }
}
