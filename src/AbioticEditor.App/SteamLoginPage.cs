namespace AbioticEditor.App;

/// <summary>
/// Modal Steam community sign-in. Hosts the real steamcommunity.com login page in a
/// WebView; on Windows it watches the WebView2 cookie store and completes as soon as a
/// <c>steamLoginSecure</c> session cookie appears (i.e. the login finished), handing the
/// captured <c>Cookie</c> header value to the caller via <see cref="Result"/>.
/// <para>
/// Privacy: the credentials are typed into Steam's own page - the app never sees them,
/// only the resulting session cookie (see <see cref="Services.SteamSession"/> for how
/// that is stored). On non-Windows platforms in-app capture isn't implemented yet, so
/// the page offers the system-browser fallback instead.
/// </para>
/// </summary>
public sealed class SteamLoginPage : ContentPage
{
    private const string LoginUrl = "https://steamcommunity.com/login/home/?goto=";
    private const string CommunityOrigin = "https://steamcommunity.com";

    private readonly TaskCompletionSource<string?> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Label _status;
    private bool _finished;
#if WINDOWS
    private WebView? _webView;
#endif

    /// <summary>
    /// Completes with the captured cookie header (at least <c>steamLoginSecure</c>, plus
    /// <c>sessionid</c> when present) on success, or null when cancelled/dismissed.
    /// </summary>
    public Task<string?> Result => _tcs.Task;

    public SteamLoginPage()
    {
        Title = "Steam sign-in";
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        var accent = (Color)Application.Current.Resources["AfAccentOrange"];
        var muted = (Color)Application.Current.Resources["AfTextSecondary"];

        _status = new Label { FontSize = 11, TextColor = muted, LineBreakMode = LineBreakMode.WordWrap };

        var cancel = new Button { Text = "CANCEL", HorizontalOptions = LayoutOptions.End };
        cancel.Clicked += async (_, _) => await FinishAsync(null);

        var grid = new Grid
        {
            Padding = new Thickness(20, 16),
            RowSpacing = 8,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
            },
        };
        grid.Add(new Label
        {
            Text = "STEAM SIGN-IN",
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            CharacterSpacing = 3,
            TextColor = accent,
        }, 0, 0);
        grid.Add(new Label
        {
            Text = "Sign in on Steam's own page below - the editor never sees your password, "
                 + "only the resulting session cookie, which stays on this device "
                 + "(OS-protected storage) and is cleared by SIGN OUT.",
            FontSize = 11,
            TextColor = muted,
            LineBreakMode = LineBreakMode.WordWrap,
        }, 0, 1);
        grid.Add(BuildLoginArea(), 0, 2);
        grid.Add(_status, 0, 3);
        grid.Add(cancel, 0, 4);

        Content = grid;
    }

#if WINDOWS
    private WebView BuildLoginArea()
    {
        var webView = new WebView { Source = LoginUrl };
        webView.Navigated += OnNavigated;
        _webView = webView;
        return webView;
    }
#else
    private static View BuildLoginArea()
    {
        // In-app capture needs the WebView2 cookie API, which only exists on Windows.
        var open = new Button { Text = "SIGN IN VIA SYSTEM BROWSER", HorizontalOptions = LayoutOptions.Center };
        open.Clicked += async (_, _) => await Launcher.Default.OpenAsync(LoginUrl);
        return new VerticalStackLayout
        {
            Spacing = 12,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "In-app Steam sign-in is currently Windows-only. Use your browser instead:",
                    FontSize = 12,
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.WordWrap,
                },
                open,
            },
        };
    }
#endif

#if WINDOWS
    /// <summary>
    /// After every navigation, peeks at the WebView2 cookie jar for steamcommunity.com.
    /// Once Steam's login flow lands and <c>steamLoginSecure</c> exists, the session is
    /// captured and the modal closes itself.
    /// </summary>
    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (_finished || sender is not WebView webView) return;
        try
        {
            if (webView.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 native) return;
            await native.EnsureCoreWebView2Async();
            var core = native.CoreWebView2;
            if (core is null) return;

            var cookies = await core.CookieManager.GetCookiesAsync(CommunityOrigin);
            string? loginSecure = null;
            string? sessionId = null;
            foreach (var cookie in cookies)
            {
                if (string.Equals(cookie.Name, "steamLoginSecure", StringComparison.OrdinalIgnoreCase))
                {
                    loginSecure = cookie.Value;
                }
                else if (string.Equals(cookie.Name, "sessionid", StringComparison.OrdinalIgnoreCase))
                {
                    sessionId = cookie.Value;
                }
            }
            if (string.IsNullOrEmpty(loginSecure)) return; // not signed in yet - keep watching

            var header = string.IsNullOrEmpty(sessionId)
                ? $"steamLoginSecure={loginSecure}"
                : $"sessionid={sessionId}; steamLoginSecure={loginSecure}";
            await FinishAsync(header);
        }
        catch (Exception ex)
        {
            // E.g. the WebView2 runtime is missing or the cookie API is unavailable.
            _status.Text = $"Could not read the sign-in session ({ex.Message}). "
                         + "Cancel and use VIEW IN BROWSER as a fallback.";
        }
    }
#endif

    /// <summary>Resolves <see cref="Result"/> (before popping, so dismissal can't race it) and closes.</summary>
    private async Task FinishAsync(string? cookieHeader)
    {
        if (_finished) return;
        _finished = true;
        _tcs.TrySetResult(cookieHeader);
        await Navigation.PopModalAsync();
    }

    /// <inheritdoc/>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Covers dismissal paths that bypass our buttons (hardware back, shell close).
        _tcs.TrySetResult(null);

#if WINDOWS
        // Release the WebView2: it holds a separate browser process + unmanaged memory that a
        // modal pop does not reliably reclaim. Detach our handler, drop the source, disconnect.
        if (_webView is { } web)
        {
            _webView = null;
            web.Navigated -= OnNavigated;
            web.Source = null;
            web.Handler?.DisconnectHandler();
        }
#endif
    }
}
