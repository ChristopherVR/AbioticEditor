namespace AbioticEditor.Core.Saves;

/// <summary>
/// Opens a save file for parsing. The save library reads a file in many small pieces, which is
/// noticeably slower against the disk than against memory (about a quarter of the load time of the
/// 16 MB Facility save), so the whole file is read in one go and parsed from memory.
/// </summary>
internal static class SaveFileStream
{
    public static MemoryStream OpenForParsing(string path)
        => new(File.ReadAllBytes(path), writable: false);
}
