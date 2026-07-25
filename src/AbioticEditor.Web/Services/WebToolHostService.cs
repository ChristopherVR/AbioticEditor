using System.Collections.Concurrent;
using System.Text;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;
using UeSaveGame;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Owns short-lived browser sessions for plugin web tools.  The browser only receives an
/// unguessable session key; paths and plugin instances remain on the host.
/// </summary>
public sealed class WebToolHostService
{
    private readonly ConcurrentDictionary<string, WebToolSession> _sessions = new(StringComparer.Ordinal);
    private readonly SaveWorkspaceSessionService _workspace;

    public WebToolHostService(SaveWorkspaceSessionService workspace) => _workspace = workspace;

    public WebToolSession Open(PluginCapability<IWebTool> capability)
    {
        var session = new WebToolSession(Guid.NewGuid().ToString("N"), capability, new WebToolContext(capability.Plugin.Host!, () => ActiveSavePath));
        session.Content = capability.Value.CreateContent(session.Context);
        if (session.Content.Html is null && session.Content.RootDirectory is null)
            session.Content = WebToolContent.FromHtml("<!doctype html><meta charset=utf-8><body>This web tool defined no content.</body>");
        _sessions[session.Key] = session;
        return session;
    }

    public void Close(string key) => _sessions.TryRemove(key, out _);

    public bool TryGet(string key, out WebToolSession? session) => _sessions.TryGetValue(key, out session);

    public async Task<string?> RequestAsync(string key, string message, CancellationToken cancellationToken)
    {
        if (!TryGet(key, out var session) || session is null)
            throw new KeyNotFoundException("The web tool session has closed.");
        return await session.Capability.Value.HandleMessageAsync(message, session.Context, cancellationToken);
    }

    public void Log(string key, string message)
    {
        if (TryGet(key, out var session) && session is not null)
            session.Capability.Plugin.Host?.Log.Info($"[web] {message}");
    }

    public string? ActiveSavePath => _workspace.Current?.SelectedSave?.Path;

    public static bool TryResolveAsset(WebToolSession session, string? asset, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(session.Content.RootDirectory)) return false;
        var root = session.Content.RootDirectory!;
        root = Path.IsPathRooted(root) ? root : Path.Combine(session.Capability.Plugin.Folder, root);
        root = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(root, string.IsNullOrWhiteSpace(asset) ? session.Content.EntryFile : asset));
        if (!candidate.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
            && !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)) return false;
        path = candidate;
        return File.Exists(path);
    }

    public static string InjectBridge(string html, string key)
    {
        var script = $"<script>{BridgeScript(key)}</script>";
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head >= 0 ? html.Insert(head, script) : script + html;
    }

    // Uses same-origin fetch rather than exposing a host object. Plugin content remains trusted,
    // while the endpoint remains local and scoped to this single open tool.
    private static string BridgeScript(string key)
    {
        var root = "/plugin-tools/" + key;
        return "window.abiotic=(function(){var root=" + System.Text.Json.JsonSerializer.Serialize(root) + ";return{" +
            "request:function(o){return fetch(root+'/request',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(o)})" +
            ".then(function(r){if(!r.ok)throw new Error('Abiotic host request failed');return r.text();})" +
            ".then(function(t){if(!t)return null;try{return JSON.parse(t);}catch(e){return t;}});}," +
            "log:function(m){return fetch(root+'/log',{method:'POST',headers:{'Content-Type':'text/plain'},body:String(m)});}," +
            "__event:null,onEvent:function(f){this.__event=f;},__fireEvent:function(t){if(this.__event){try{this.__event(typeof t==='string'?JSON.parse(t):t);}catch(e){}}}" +
            "};})();";
    }
}

public sealed class WebToolSession
{
    internal WebToolSession(string key, PluginCapability<IWebTool> capability, WebToolContext context)
    { Key = key; Capability = capability; Context = context; }
    public string Key { get; }
    public PluginCapability<IWebTool> Capability { get; }
    public WebToolContext Context { get; }
    public WebToolContent Content { get; internal set; } = WebToolContent.FromHtml(string.Empty);
}

/// <summary>Live, cached view of the selected save exposed to IWebTool implementations.</summary>
public sealed class WebToolContext : IWebToolContext
{
    private readonly Func<string?> _pathProvider;
    private string? _cachedPath;
    private DateTime _cachedWriteTimeUtc;
    private SaveGame? _cachedSave;

    internal WebToolContext(IPluginHost host, Func<string?> pathProvider) { Host = host; _pathProvider = pathProvider; }
    public IPluginHost Host { get; }
    public string? ActiveSavePath => _pathProvider();
    public SaveKind? ActiveSaveKind => ActiveSavePath is { } path ? SaveKindDetector.Detect(path) : null;
    public SaveGame? ActiveSave
    {
        get
        {
            var path = ActiveSavePath;
            if (path is null) { _cachedPath = null; _cachedSave = null; return null; }
            try
            {
                var timestamp = File.GetLastWriteTimeUtc(path);
                if (_cachedSave is not null && string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) && timestamp == _cachedWriteTimeUtc) return _cachedSave;
                Core.SaveClasses.AbioticSaveClasses.EnsureLoaded();
                using var stream = File.OpenRead(path);
                _cachedSave = SaveGame.LoadFrom(stream);
                _cachedPath = path;
                _cachedWriteTimeUtc = timestamp;
                return _cachedSave;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException)
            {
                Host.Log.Warn($"web tool could not load active save '{path}': {ex.Message}");
                _cachedPath = null; _cachedSave = null;
                return null;
            }
        }
    }
}
