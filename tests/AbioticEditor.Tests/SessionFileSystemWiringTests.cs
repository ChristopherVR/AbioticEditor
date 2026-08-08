using System.Text.RegularExpressions;

namespace AbioticEditor.Tests;

/// <summary>
/// Every save session the editor builds must be handed the host's file system.
/// </summary>
/// <remarks>
/// The file system argument is optional, and when it is left off the session writes straight to
/// a disk path instead. That is invisible on the desktop, where writing to a path is exactly
/// right, and broken in a browser, which has no paths at all - it only has the folder handles the
/// player granted.
///
/// It cost a real bug: sending a carried pet to a bed in another world staged correctly, reported
/// success, and then failed at SAVE WORLD, because the session for the receiving world had been
/// built without one.
/// </remarks>
public sealed class SessionFileSystemWiringTests
{
    [Fact]
    public void EverySaveSession_IsGivenTheHostsFileSystem()
    {
        var root = Path.Combine(UiSource.RepositoryRoot, "src");
        var construction = new Regex(@"new (World|Player)SaveSession\((?<args>[^;]*?)\)\s*;", RegexOptions.Singleline);
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                         StringComparison.Ordinal)))
        {
            foreach (Match match in construction.Matches(File.ReadAllText(file)))
            {
                var args = match.Groups["args"].Value;
                // The file system is passed as the field/parameter holding it. Naming it is
                // enough: this guards the omission, not the exact variable chosen.
                if (!Regex.IsMatch(args, @"\b_?files\b", RegexOptions.IgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value.Split('\n')[0].Trim()}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These sessions are built without the host's file system, so saving them writes to a "
            + "disk path directly. That is silently correct on the desktop and cannot work in a "
            + $"browser:{Environment.NewLine}" + string.Join(Environment.NewLine, offenders));
    }
}
