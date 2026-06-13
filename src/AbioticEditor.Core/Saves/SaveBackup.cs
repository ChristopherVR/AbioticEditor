namespace AbioticEditor.Core.Saves;

/// <summary>
/// Pre-write backups: every editor write first copies the existing file to
/// <c>&lt;name&gt;.sav.bak</c> (overwriting any earlier backup). One level deep is enough
/// to recover from a corrupting write while never growing unbounded.
/// </summary>
public static class SaveBackup
{
    public static void CreateFor(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
        }
        catch
        {
            // A failed backup must not block the save itself.
        }
    }
}
