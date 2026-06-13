using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core.Steam;

namespace AbioticEditor.App.ViewModels;

/// <summary>One achievement row with spoiler masking and comparison state.</summary>
public sealed class AchievementRowViewModel : INotifyPropertyChanged
{
    private readonly AchievementsViewModel _owner;

    public AchievementRowViewModel(AchievementsViewModel owner, string apiName)
    {
        _owner = owner;
        ApiName = apiName;
    }

    public string ApiName { get; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Hidden { get; set; }
    public bool Unlocked { get; set; }
    public string? IconUrl { get; set; }
    public string? IconGrayUrl { get; set; }
    public DateTimeOffset? UnlockedAt { get; set; }

    /// <summary>Other player's unlock state (null = no comparison loaded).</summary>
    public bool? CompareUnlocked { get; set; }

    // ---------- display ----------

    /// <summary>Per-item reveal key for the spoiler service.</summary>
    public string SpoilerKey => SpoilerService.Key(SpoilerService.Achievement, ApiName);

    /// <summary>
    /// Spoiler-concealed: a hidden achievement the player hasn't earned, while protection
    /// is on and this one hasn't been individually revealed.
    /// </summary>
    public bool IsConcealed => SpoilerService.ShouldConceal(SpoilerKey, Hidden && !Unlocked);

    public string ShownTitle => IsConcealed ? SpoilerService.ClassifiedTitle : DisplayName;
    public string? ShownDescription => IsConcealed
        ? $"Hidden achievement. {SpoilerService.ClassifiedHint}"
        : string.IsNullOrEmpty(Description) && Hidden ? "(hidden achievement)" : Description;

    public string? ShownIcon => IsConcealed ? null : (Unlocked ? IconUrl : IconGrayUrl ?? IconUrl);
    public bool HasIcon => ShownIcon is not null;

    /// <summary>Tap a sealed row to override clearance and reveal it permanently.</summary>
    public ICommand RevealCommand => _reveal ??= new RelayCommand(async () =>
    {
        if (!IsConcealed) return;
        if (await SpoilerPrompt.RevealAsync("This hidden achievement", SpoilerKey)) NotifyAll();
    });
    private RelayCommand? _reveal;

    public string StatusLabel => Unlocked ? "UNLOCKED" : "LOCKED";
    public string UnlockedAtText => UnlockedAt is { } t
        ? t.LocalDateTime.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    public bool HasCompare => CompareUnlocked is not null;
    public string CompareLabel => CompareUnlocked switch
    {
        true => $"{_owner.CompareName}: ✓",
        false => $"{_owner.CompareName}: ✗",
        _ => string.Empty,
    };

    internal void NotifyAll()
    {
        foreach (var p in new[]
        {
            nameof(ShownTitle), nameof(ShownDescription), nameof(ShownIcon), nameof(HasIcon),
            nameof(IsConcealed), nameof(StatusLabel), nameof(UnlockedAtText),
            nameof(HasCompare), nameof(CompareLabel), nameof(Unlocked),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Achievements for one player: local Steam-cache read merged with the public
/// community web endpoint (authoritative + provides icons), spoiler handling,
/// search, and comparison against other accounts found in the save folder.
/// </summary>
public sealed class AchievementsViewModel : INotifyPropertyChanged
{
    public sealed record CompareCandidate(long SteamId64, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly long _steamId;
    private readonly Dictionary<string, AchievementRowViewModel> _rows = new(StringComparer.OrdinalIgnoreCase);
    private bool _loadRequested;
    private string? _status;
    private string _searchText = string.Empty;
    private CompareCandidate? _selectedCompare;
    private string _compareName = "OTHER";
    private IReadOnlyList<AchievementRowViewModel> _visible = Array.Empty<AchievementRowViewModel>();

    public AchievementsViewModel(long steamId64, IReadOnlyList<CompareCandidate> compareCandidates)
    {
        _steamId = steamId64;
        CompareCandidates = compareCandidates;
        CheckSteamCommand = new RelayCommand(async () => await CheckSteamAsync(), () => _steamId != 0);
        // Preferred path: sign in inside the app and reuse that session for the query.
        SignInInAppCommand = new RelayCommand(async () => await SignInInAppAsync());
        // The anonymous query can't see gated profiles; a signed-in browser session can
        // see profiles Steam shares with the viewer (own account, friends).
        SignInAndViewCommand = new RelayCommand(async () => await Launcher.Default.OpenAsync(
            $"https://steamcommunity.com/login/home/?goto=profiles/{_steamId}/stats/{AppId}/achievements"));
        OpenPrivacySettingsCommand = new RelayCommand(async () => await Launcher.Default.OpenAsync(
            "https://steamcommunity.com/my/edit/settings"));
        SignOutCommand = new RelayCommand(() =>
        {
            SteamSession.SignOut();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSignedIn)));
            Status = "Signed out - the Steam session cookie was cleared. CHECK STEAM now queries anonymously again.";
        });
    }

    /// <summary>Abiotic Factor's Steam app id.</summary>
    private const int AppId = 427410;

    private bool _profileGated;

    /// <summary>
    /// True after Steam denied the anonymous stats query - shows the sign-in /
    /// privacy prompt under the status line.
    /// </summary>
    public bool ProfileGated { get => _profileGated; private set => Set(ref _profileGated, value); }

    /// <summary>Opens the in-app Steam sign-in modal, then re-runs the Steam check.</summary>
    public ICommand SignInInAppCommand { get; }

    /// <summary>Opens Steam community sign-in, then redirects to this profile's achievements.</summary>
    public ICommand SignInAndViewCommand { get; }

    /// <summary>Opens the signed-in user's Steam privacy settings page.</summary>
    public ICommand OpenPrivacySettingsCommand { get; }

    /// <summary>Clears the captured Steam session cookie (memory and secure storage).</summary>
    public ICommand SignOutCommand { get; }

    /// <summary>True when a Steam session cookie is held - CHECK STEAM queries as that user.</summary>
#pragma warning disable CA1822 // XAML-bound: Binding paths need an instance property, and PropertyChanged is raised per instance.
    public bool IsSignedIn => SteamSession.IsSignedIn;
#pragma warning restore CA1822

    public IReadOnlyList<CompareCandidate> CompareCandidates { get; }
    public bool HasCompareCandidates => CompareCandidates.Count > 0;

    public ICommand CheckSteamCommand { get; }

    public string? Status { get => _status; private set => Set(ref _status, value); }
    public string CompareName => _compareName;

    public string Summary
    {
        get
        {
            if (_rows.Count == 0) return string.Empty;
            var unlocked = _rows.Values.Count(r => r.Unlocked);
            return $"{unlocked} of {_rows.Count} unlocked";
        }
    }

    public IReadOnlyList<AchievementRowViewModel> Visible
    {
        get => _visible;
        private set => Set(ref _visible, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (Set(ref _searchText, value ?? string.Empty)) ApplyFilter();
        }
    }

    /// <summary>
    /// Convenience mirror of the app-wide spoiler setting (checked = protection off = all
    /// hidden achievements shown). The authoritative control lives in SETTINGS; flipping it
    /// here changes it everywhere.
    /// </summary>
    public bool ShowSpoilers
    {
        get => !SpoilerService.Enabled;
        set
        {
            var enabled = !value;
            if (SpoilerService.Enabled == enabled) return;
            SpoilerService.Enabled = enabled;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowSpoilers)));
            foreach (var r in _rows.Values) r.NotifyAll();
        }
    }

    public CompareCandidate? SelectedCompare
    {
        get => _selectedCompare;
        set
        {
            if (Set(ref _selectedCompare, value) && value is not null)
            {
                _ = LoadCompareAsync(value);
            }
        }
    }

    // ---------- loading ----------

    /// <summary>Local cache first (instant when present), then merge state lazily.</summary>
    public void EnsureLoaded()
    {
        if (_loadRequested) return;
        _loadRequested = true;

        if (_steamId == 0)
        {
            Status = "Could not determine the SteamID from the file name.";
            return;
        }

        Status = "Reading Steam's local achievement cache…";
        _ = Task.Run(() =>
        {
            var local = SteamAchievements.LoadFor(_steamId);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (local is null)
                {
                    Status = "No local Steam cache for this account on this machine - use CHECK STEAM to query the web (public profiles only).";
                }
                else
                {
                    MergeLocal(local);
                    Status = "Local Steam cache (icons via Steam CDN) · the cache only updates while Steam runs - CHECK STEAM verifies the live state online.";
                }
                NotifyCollection();
            });
        });
    }

    private async Task CheckSteamAsync()
    {
        var cookie = SteamSession.CookieHeader;
        try
        {
            Status = cookie is null
                ? "Querying steamcommunity.com…"
                : "Querying steamcommunity.com (signed in)…";
            var web = await SteamWebAchievements.FetchAsync(_steamId, cookieHeader: cookie);
            MergeWeb(web);
            ProfileGated = false;
            Status = $"Steam web data loaded ({web.Count(a => a.Unlocked)} unlocked on Steam) · icons from steamcommunity.com.";
            NotifyCollection();
        }
        catch (SteamGameDetailsPrivateException)
        {
            // Anonymous (or insufficient) sessions get gated profiles withheld even
            // when the user could see them while signed in - offer that path too.
            ProfileGated = true;
            Status = cookie is null
                ? "Steam blocked the anonymous stats query for this account. Either sign in below "
                  + "(a logged-in session can view its own and friends' achievements), or make the "
                  + "profile's 'Game details' dropdown Public "
                  + "(Steam → Profile → Edit Profile → Privacy Settings → Game details; "
                  + "changes can take a few minutes to apply). Local cache data is still shown."
                : "Steam still blocked the stats query despite the signed-in session - the session may "
                  + "have expired (SIGN OUT, then sign in again), or this profile isn't visible to that "
                  + "account. Local cache data is still shown.";
        }
        catch (Exception ex)
        {
            Status = $"Steam check failed: {ex.Message} - local cache data is still shown.";
        }
    }

    /// <summary>
    /// Pushes the modal in-app Steam login; on success stores the session and immediately
    /// re-runs the Steam check with it.
    /// </summary>
    private async Task SignInInAppAsync()
    {
        var windows = Application.Current?.Windows;
        var page = windows is { Count: > 0 } ? windows[0].Page : null;
        if (page is null)
        {
            Status = "Could not open the sign-in window.";
            return;
        }

        var login = new SteamLoginPage();
        await page.Navigation.PushModalAsync(login);
        var cookie = await login.Result;
        if (cookie is null)
        {
            Status = "Steam sign-in was cancelled.";
            return;
        }

        SteamSession.SignIn(cookie);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSignedIn)));
        Status = "Signed in to Steam - re-checking achievements with the session…";
        await CheckSteamAsync();
    }

    private async Task LoadCompareAsync(CompareCandidate candidate)
    {
        _compareName = candidate.Label.ToUpperInvariant();
        try
        {
            Status = $"Loading comparison for {candidate.Label}…";
            // Local cache first; fall back to the web for accounts never used here.
            var local = SteamAchievements.LoadFor(candidate.SteamId64);
            HashSet<string> otherUnlocked;
            if (local is not null)
            {
                otherUnlocked = local.Where(a => a.Unlocked).Select(a => a.ApiName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var web = await SteamWebAchievements.FetchAsync(
                    candidate.SteamId64, cookieHeader: SteamSession.CookieHeader);
                otherUnlocked = web.Where(a => a.Unlocked).Select(a => a.ApiName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var r in _rows.Values)
            {
                r.CompareUnlocked = otherUnlocked.Contains(r.ApiName);
                r.NotifyAll();
            }
            var ahead = _rows.Values.Count(r => r.Unlocked && r.CompareUnlocked == false);
            var behind = _rows.Values.Count(r => !r.Unlocked && r.CompareUnlocked == true);
            Status = $"Compared with {candidate.Label}: you have {ahead} they don't; they have {behind} you don't.";
        }
        catch (Exception ex)
        {
            Status = $"Comparison failed: {ex.Message}";
            foreach (var r in _rows.Values)
            {
                r.CompareUnlocked = null;
                r.NotifyAll();
            }
        }
    }

    private void MergeLocal(IReadOnlyList<AchievementState> local)
    {
        foreach (var a in local)
        {
            var row = GetRow(a.ApiName);
            row.DisplayName = a.DisplayName;
            row.Description ??= a.Description;
            row.Hidden = a.Hidden;
            row.Unlocked |= a.Unlocked;
            // The local schema carries the same CDN icon hashes Steam uses.
            row.IconUrl ??= a.IconUrl;
            row.IconGrayUrl ??= a.IconGrayUrl;
        }
        ApplyFilter();
    }

    private void MergeWeb(IReadOnlyList<WebAchievement> web)
    {
        foreach (var a in web)
        {
            var row = GetRow(a.ApiName);
            if (!string.IsNullOrEmpty(a.DisplayName)) row.DisplayName = a.DisplayName;
            if (!string.IsNullOrEmpty(a.Description)) row.Description = a.Description;
            row.Unlocked = a.Unlocked; // web is authoritative
            row.IconUrl = a.IconUrl;
            row.IconGrayUrl = a.IconGrayUrl;
            row.UnlockedAt = a.UnlockedAt;
        }
        ApplyFilter();
    }

    private AchievementRowViewModel GetRow(string apiName)
    {
        if (!_rows.TryGetValue(apiName, out var row))
        {
            row = new AchievementRowViewModel(this, apiName);
            _rows[apiName] = row;
        }
        return row;
    }

    private void ApplyFilter()
    {
        IEnumerable<AchievementRowViewModel> q = _rows.Values;
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var f = _searchText.Trim();
            q = q.Where(r =>
                r.ApiName.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (!r.IsConcealed && r.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase))
                || (!r.IsConcealed && (r.Description?.Contains(f, StringComparison.OrdinalIgnoreCase) ?? false)));
        }
        Visible = q
            .OrderByDescending(r => r.Unlocked)
            .ThenBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var r in _visible) r.NotifyAll();
    }

    private void NotifyCollection()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
        ApplyFilter();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
