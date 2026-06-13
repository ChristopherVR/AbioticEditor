using UeSaveGame;

namespace AbioticEditor.Plugins.Ui;

/// <summary>
/// A UI tool whose interface is <b>HTML</b> rendered in a web view, rather than native MAUI
/// controls. This is how a plugin ships a rich web UI - including a <b>React</b> (or Vue,
/// Svelte, plain HTML/CSS) front-end - and is especially powerful for JavaScript plugins,
/// which can supply both the page and the logic behind it.
///
/// <para>
/// The host renders <see cref="CreateContent"/> in a web view and wires a small bridge so the
/// page can talk back: page JavaScript calls <c>abiotic.request(obj)</c> (returns a Promise),
/// the host routes the payload to <see cref="HandleMessageAsync"/>, and the reply resolves the
/// Promise. The host also pushes events to the page (e.g. when the open save changes). This
/// keeps the contract host-agnostic - the SDK never references a web-view type; it only moves
/// HTML in and strings across the bridge.
/// </para>
///
/// <para>
/// Like every UI capability, web tools run only in GUI hosts; the CLI ignores them. And like
/// all plugins, the page runs in-process with full trust - it is not a security sandbox.
/// </para>
/// </summary>
public interface IWebTool
{
    /// <summary>Stable id (kebab-case), unique within the plugin.</summary>
    string Id { get; }

    /// <summary>Title shown on the tool's entry in the Plugins panel and the host window.</summary>
    string Title { get; }

    /// <summary>Optional single-glyph icon shown next to the title.</summary>
    string? Glyph => null;

    /// <summary>
    /// Produces the page to render: either inline <see cref="WebToolContent.Html"/> (the host
    /// injects the bridge before it, so React-from-a-CDN works out of the box) or a
    /// <see cref="WebToolContent.RootDirectory"/> of static assets (a built React/SPA bundle).
    /// Called on the UI thread when the tool opens.
    /// </summary>
    WebToolContent CreateContent(IWebToolContext context);

    /// <summary>
    /// Handles one request from the page (the payload passed to <c>abiotic.request</c>) and
    /// returns a reply string (typically JSON) that resolves the page's Promise. Return null
    /// for "no reply". This is the read/compute channel - to *edit* a save, register an
    /// <see cref="Saves.ISaveOperation"/> and have the page ask the host to run it, so the edit
    /// still goes through the backup/write path.
    /// </summary>
    Task<string?> HandleMessageAsync(string message, IWebToolContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// What an <see cref="IWebTool"/> renders. Exactly one of <see cref="Html"/> or
/// <see cref="RootDirectory"/> should be set.
/// </summary>
public sealed record WebToolContent
{
    /// <summary>Inline HTML for the page. The host injects the bridge script before it.</summary>
    public string? Html { get; init; }

    /// <summary>
    /// A directory of static web assets (e.g. a production React build). The host serves
    /// <see cref="EntryFile"/> from it and best-effort injects the bridge after load.
    /// </summary>
    public string? RootDirectory { get; init; }

    /// <summary>Entry file within <see cref="RootDirectory"/>. Defaults to <c>index.html</c>.</summary>
    public string EntryFile { get; init; } = "index.html";

    /// <summary>Builds inline-HTML content.</summary>
    public static WebToolContent FromHtml(string html) => new() { Html = html };

    /// <summary>Builds directory-served content (a bundled web app).</summary>
    public static WebToolContent FromDirectory(string rootDirectory, string entryFile = "index.html")
        => new() { RootDirectory = rootDirectory, EntryFile = entryFile };
}

/// <summary>
/// Context for an <see cref="IWebTool"/>: host services plus a read-only window onto the open
/// save, so the page can render live information by asking <see cref="IWebTool.HandleMessageAsync"/>.
/// </summary>
public interface IWebToolContext
{
    /// <summary>Host services (logging, data directory, versions).</summary>
    IPluginHost Host { get; }

    /// <summary>Absolute path of the open save, or null when none is open.</summary>
    string? ActiveSavePath { get; }

    /// <summary>Detected kind of the open save, or null when none is open.</summary>
    SaveKind? ActiveSaveKind { get; }

    /// <summary>
    /// The open save, lazily loaded, or null when none is open. A web tool that wants to
    /// *edit* should route through an <see cref="Saves.ISaveOperation"/> rather than mutating
    /// this directly.
    /// </summary>
    SaveGame? ActiveSave { get; }
}
