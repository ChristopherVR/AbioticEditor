namespace AbioticEditor.App.Services;

/// <summary>
/// App-wide spoiler protection. When enabled, content the player hasn't reached yet
/// (locked quest flags, unavailable traders, locked recipes, hidden achievements, gated
/// codex entries, contained anomalies) is presented as facility-classified until the
/// user deliberately overrides clearance on that one item. An override is remembered
/// across sessions, so a thing the user chooses to see stays visible going forward.
///
/// In-universe framing: the GATE Cascade Research Facility seals data above the player's
/// current clearance. Concealed surfaces read CLASSIFIED; revealing is an OVERRIDE.
///
/// Static + Preferences-backed, mirroring <see cref="ThemeService"/>; the CLI has its own
/// (flag-driven) handling since it runs in a separate process with no shared storage.
/// </summary>
public static class SpoilerService
{
    private const string EnabledKey = "SpoilerProtectionEnabled";
    private const string RevealedKey = "SpoilerRevealedKeys";

    // Namespaces for revealed-item keys so ids from different surfaces never collide.
    public const string Flag = "flag";
    public const string Trader = "trader";
    public const string Recipe = "recipe";
    public const string Achievement = "ach";
    public const string Codex = "codex";
    public const string Containment = "containment";
    public const string Skill = "skill";

    /// <summary>Lore-flavoured mask copy (the facility's redaction stamps).</summary>
    public const string ClassifiedTitle = "▓ CLASSIFIED — CLEARANCE REQUIRED";
    public const string ClassifiedShort = "▓ CLASSIFIED";
    public const string Redacted = "[ DATA EXPUNGED ]";
    public const string ClassifiedHint = "Above your current clearance. Tap to override and reveal (you'll keep seeing it).";

    private static bool _loaded;
    private static bool _enabled = true;
    private static HashSet<string> _revealed = new(StringComparer.Ordinal);

    /// <summary>Raised when the master toggle flips or any item is revealed/re-sealed.</summary>
    public static event Action? Changed;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _enabled = Preferences.Default.Get(EnabledKey, true);
        var stored = Preferences.Default.Get(RevealedKey, string.Empty);
        _revealed = string.IsNullOrEmpty(stored)
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                stored.Split('\n', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
    }

    /// <summary>Master switch. Defaults ON so first-run installs are protected.</summary>
    public static bool Enabled
    {
        get { EnsureLoaded(); return _enabled; }
        set
        {
            EnsureLoaded();
            if (_enabled == value) return;
            _enabled = value;
            Preferences.Default.Set(EnabledKey, value);
            Changed?.Invoke();
        }
    }

    /// <summary>Builds a stable revealed-item key, e.g. <c>flag:Office_NewGameStarted</c>.</summary>
    public static string Key(string ns, string id) => $"{ns}:{id}";

    /// <summary>Has the user overridden clearance on this item before?</summary>
    public static bool IsRevealed(string key)
    {
        EnsureLoaded();
        return _revealed.Contains(key);
    }

    /// <summary>
    /// Should this item be presented as classified? True only when protection is on, the
    /// item is content the player hasn't reached, and it hasn't been individually revealed.
    /// </summary>
    public static bool ShouldConceal(string key, bool isFutureContent)
    {
        EnsureLoaded();
        return _enabled && isFutureContent && !_revealed.Contains(key);
    }

    /// <summary>Records a permanent clearance override for one item.</summary>
    public static void Reveal(string key)
    {
        EnsureLoaded();
        if (!_revealed.Add(key)) return;
        Persist();
        Changed?.Invoke();
    }

    /// <summary>Re-seals every individually revealed item (the master toggle is untouched).</summary>
    public static void ResetReveals()
    {
        EnsureLoaded();
        if (_revealed.Count == 0) return;
        _revealed.Clear();
        Persist();
        Changed?.Invoke();
    }

    /// <summary>Count of items the user has revealed (for the settings summary).</summary>
    public static int RevealedCount
    {
        get { EnsureLoaded(); return _revealed.Count; }
    }

    /// <summary>Returns <paramref name="original"/> unless concealed, then the placeholder.</summary>
    public static string Mask(string original, bool concealed, string? placeholder = null)
        => concealed ? (placeholder ?? Redacted) : original;

    private static void Persist()
        => Preferences.Default.Set(RevealedKey, string.Join('\n', _revealed));
}
