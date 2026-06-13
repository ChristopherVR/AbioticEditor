using System.Text.Json;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.App;

/// <summary>
/// Hosts an <see cref="IWebTool"/>'s HTML (which may be a React/SPA app) in a MAUI
/// <see cref="WebView"/>, and bridges page JavaScript to the plugin.
///
/// <para>
/// The bridge uses custom-scheme navigation, which works across every MAUI platform without a
/// platform-specific message channel: page JS sets <c>location.href</c> to an
/// <c>abiotic://...</c> URL, this page intercepts it in <see cref="WebView.Navigating"/>
/// (cancelling the real navigation), routes the payload to
/// <see cref="IWebTool.HandleMessageAsync"/>, then resolves the page's Promise by evaluating
/// <c>window.abiotic.__resolve(id, reply)</c>. A small bridge script (<see cref="BridgeJs"/>)
/// provides <c>abiotic.request()</c>/<c>.log()</c>/<c>.onEvent()</c> to the page.
/// </para>
/// </summary>
internal sealed class WebToolHostPage : ModalCleanupPage
{
    private readonly PluginCapability<IWebTool> _capability;
    private readonly IWebToolContext _context;
    private readonly WebView _web;

    public WebToolHostPage(PluginCapability<IWebTool> capability, IWebToolContext context)
    {
        _capability = capability;
        _context = context;
        Title = capability.Value.Title;
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];

        _web = new WebView { VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Fill };
        _web.Navigating += OnNavigating;
        _web.Navigated += OnNavigated;
        _web.Source = BuildSource(capability.Value.CreateContent(context));

        var close = new Button { Text = "CLOSE", Margin = new Thickness(12) };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var grid = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
        };
        grid.Add(_web, 0, 0);
        grid.Add(close, 0, 1);
        Content = grid;
    }

    /// <summary>
    /// Releases the WebView when the page is genuinely closed (not when a plugin pushes a modal
    /// over it - see <see cref="ModalCleanupPage"/>). A MAUI <see cref="WebView"/> on Windows
    /// wraps a WebView2 control backed by a separate browser process and substantial unmanaged
    /// memory; popping the modal does NOT reliably dispose it, so without this teardown every
    /// open/close of a web tool would leak a WebView2 instance. We detach our handlers, drop the
    /// source, and disconnect the platform handler, then dispose the context if it owns anything.
    /// </summary>
    protected override void OnModalRemoved()
    {
        _web.Navigating -= OnNavigating;
        _web.Navigated -= OnNavigated;
        _web.Source = null;
        _web.Handler?.DisconnectHandler();

        (_context as IDisposable)?.Dispose();
    }

    private WebViewSource BuildSource(WebToolContent content)
    {
        if (!string.IsNullOrEmpty(content.Html))
        {
            // Inject the bridge before the plugin's markup so a React app can call it on mount.
            return new HtmlWebViewSource { Html = $"<script>{BridgeJs}</script>\n{content.Html}" };
        }
        if (!string.IsNullOrEmpty(content.RootDirectory))
        {
            // A relative root is resolved against the plugin's own install folder, so a plugin
            // can ship its web assets in a subfolder (e.g. rootDirectory: "web").
            var root = Path.IsPathRooted(content.RootDirectory)
                ? content.RootDirectory
                : Path.Combine(_capability.Plugin.Folder, content.RootDirectory);
            var entry = Path.Combine(root, content.EntryFile);
            return new UrlWebViewSource { Url = new Uri(entry).AbsoluteUri };
        }
        return new HtmlWebViewSource { Html = $"<script>{BridgeJs}</script><body>This web tool defined no content.</body>" };
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        // For directory-served pages we can't prepend the bridge, so inject it after load.
        try
        {
            await _web.EvaluateJavaScriptAsync($"if(!window.abiotic){{{BridgeJs}}}");
        }
        catch (Exception ex)
        {
            _capability.Plugin.Host?.Log.Warn($"web tool bridge injection failed: {ex.Message}");
        }
    }

    private void OnNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (e.Url is not { } url || !url.StartsWith("abiotic://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Cancel synchronously (before any await) so the web view never tries to load the URL.
        e.Cancel = true;
        _ = HandleBridgeAsync(url);
    }

    private async Task HandleBridgeAsync(string url)
    {
        try
        {
            var uri = new Uri(url);
            var action = uri.Host;                              // "request" or "log"
            var query = ParseQuery(uri.Query);
            var body = query.GetValueOrDefault("body", string.Empty);

            if (string.Equals(action, "log", StringComparison.OrdinalIgnoreCase))
            {
                _capability.Plugin.Host?.Log.Info($"[web] {body}");
                return;
            }

            if (string.Equals(action, "request", StringComparison.OrdinalIgnoreCase))
            {
                var id = query.GetValueOrDefault("id", "0");
                var reply = await _capability.Value.HandleMessageAsync(body, _context) ?? string.Empty;
                // JSON-encode the reply into a safe JS string literal, then resolve the Promise.
                var literal = JsonSerializer.Serialize(reply);
                await _web.EvaluateJavaScriptAsync($"window.abiotic && window.abiotic.__resolve({JsonSerializer.Serialize(id)}, {literal})");
            }
        }
        catch (Exception ex)
        {
            _capability.Plugin.Host?.Log.Error("web tool bridge request failed", ex);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            result[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        return result;
    }

    /// <summary>
    /// The bridge injected into every hosted page. Gives the page <c>abiotic.request(obj)</c>
    /// (Promise), <c>abiotic.log(msg)</c>, and <c>abiotic.onEvent(fn)</c>, talking to the host
    /// over custom-scheme navigations the page never actually follows.
    /// </summary>
    private const string BridgeJs =
        "window.abiotic=(function(){var s=0,c={};function n(u){window.location.href=u;}return{" +
        "request:function(o){return new Promise(function(r){var i=String(++s);c[i]=r;" +
        "n('abiotic://request?id='+i+'&body='+encodeURIComponent(JSON.stringify(o)));});}," +
        "__resolve:function(i,t){var f=c[i];if(f){delete c[i];try{f(JSON.parse(t));}catch(e){f(t);}}}," +
        "log:function(m){n('abiotic://log?id=0&body='+encodeURIComponent(String(m)));}," +
        "__event:null,onEvent:function(f){this.__event=f;}," +
        "__fireEvent:function(t){if(this.__event){try{this.__event(JSON.parse(t));}catch(e){}}}" +
        "};})();";
}
