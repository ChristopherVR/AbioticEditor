namespace AbioticEditor.Web.Services;

/// <summary>
/// Where the editor keeps the handful of small choices a player makes about the editor itself -
/// display language, theme, which language game names are shown in.
/// </summary>
/// <remarks>
/// <para>The desktop keeps them as one-line files under the user's local application data, which
/// is exactly right there and useless in a browser: the file system a WebAssembly app sees lives
/// in memory and is thrown away the moment the tab reloads. Since choosing a display language
/// reloads the page, the setting was gone before it could ever be read back - the editor came
/// back in the language it started in, every time.</para>
///
/// <para>So the reading and writing is a seam. Nothing is installed by default, which keeps the
/// desktop on the files it has always used; the browser host installs a pair backed by the
/// browser's own storage at startup, before any of these services are built.</para>
/// </remarks>
public static class HostPreferenceStore
{
    private static Func<string, string?>? _read;
    private static Action<string, string?>? _write;

    /// <summary>
    /// Routes every preference through <paramref name="read"/>/<paramref name="write"/> instead
    /// of the local file system. Call once, before the services that read preferences are built.
    /// A null value handed to <paramref name="write"/> means "forget this one".
    /// </summary>
    public static void UseStore(Func<string, string?> read, Action<string, string?> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _read = read;
        _write = write;
    }

    /// <summary>Resets to the file-backed default. For tests, which must not leak into each other.</summary>
    public static void UseFiles()
    {
        _read = null;
        _write = null;
    }

    /// <summary>The saved value for <paramref name="key"/>, or null when nothing was saved.</summary>
    /// <param name="fileName">
    /// The file this preference has always lived in on the desktop. Kept as the fallback so an
    /// existing install's settings survive; ignored once a store is installed.
    /// </param>
    public static string? Read(string key, string fileName)
    {
        if (_read is { } read)
        {
            var value = read(key);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        try
        {
            var path = FilePath(fileName);
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path).Trim();
            return text.Length == 0 ? null : text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves <paramref name="value"/>, or forgets the preference when it is null.</summary>
    public static void Write(string key, string fileName, string? value)
    {
        if (_write is { } write)
        {
            write(key, value);
            return;
        }
        var path = FilePath(fileName);
        if (value is null)
        {
            try { File.Delete(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value);
    }

    private static string FilePath(string fileName) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbioticEditor", fileName);

    /// <summary>Keys, shared with the browser host's start-up script so both agree on the names.</summary>
    public static class Keys
    {
        /// <summary>The editor's own display language. Read by <c>index.html</c> too, before boot.</summary>
        public const string Language = "abiotic.language";
        public const string Theme = "abiotic.theme";
        public const string Accent = "abiotic.accent";
        public const string GameDataLanguage = "abiotic.gamedatalanguage";
    }
}
