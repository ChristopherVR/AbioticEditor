namespace AbioticEditor.App.Services;

/// <summary>
/// Holds the Steam community sign-in cookie captured by <see cref="AbioticEditor.App.SteamLoginPage"/> so
/// achievement queries can run as the signed-in user instead of anonymously.
/// <para>
/// Privacy note: the cookie grants access to the user's Steam community session, so it
/// is treated like a credential. It lives in memory and - when the platform allows - in
/// OS-protected storage (<see cref="SecureStorage"/>) so sign-in survives restarts.
/// SIGN OUT clears both. If secure storage is unavailable on this machine the session
/// silently degrades to memory-only and is forgotten when the app closes.
/// </para>
/// </summary>
public static class SteamSession
{
    private const string StorageKey = "SteamSessionCookie";
    private static readonly object Gate = new();
    private static string? _cookieHeader;
    private static bool _loaded;

    /// <summary>
    /// The captured <c>Cookie</c> request header value (at least <c>steamLoginSecure</c>),
    /// or null when not signed in. First access lazily restores a persisted session.
    /// </summary>
    public static string? CookieHeader
    {
        get
        {
            EnsureLoaded();
            return _cookieHeader;
        }
    }

    /// <summary>True when a Steam session cookie is available.</summary>
    public static bool IsSignedIn => CookieHeader is not null;

    /// <summary>Stores a freshly captured cookie header and tries to persist it.</summary>
    public static void SignIn(string cookieHeader)
    {
        lock (Gate)
        {
            _cookieHeader = cookieHeader;
            _loaded = true;
        }
        _ = PersistAsync(cookieHeader);
    }

    /// <summary>Forgets the session cookie, both in memory and in secure storage.</summary>
    public static void SignOut()
    {
        lock (Gate)
        {
            _cookieHeader = null;
            _loaded = true;
        }
        try
        {
            SecureStorage.Default.Remove(StorageKey);
        }
        catch (Exception)
        {
            // Secure storage being broken just means there was nothing persisted anyway.
        }
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                // Off the captured context: SecureStorage reads a DPAPI-protected file on
                // Windows, so this completes quickly without touching the UI thread.
                _cookieHeader = Task.Run(() => SecureStorage.Default.GetAsync(StorageKey))
                    .GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // SecureStorage can throw on some machines (e.g. broken key store) -
                // degrade to memory-only: the user just has to sign in again.
                _cookieHeader = null;
            }
        }
    }

    private static async Task PersistAsync(string cookieHeader)
    {
        try
        {
            await SecureStorage.Default.SetAsync(StorageKey, cookieHeader).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Persisting is best-effort; the in-memory session keeps working.
        }
    }
}
