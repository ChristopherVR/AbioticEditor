using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Hands the 3D base view real game models when an optional plugin supplies them (an
/// <see cref="ISceneModelProvider"/>). Nothing here reads game files itself: without such a plugin
/// every call answers "not available" and the view keeps drawing boxes.
/// </summary>
/// <remarks>
/// Also installs <see cref="PluginHostEnvironment.GameAssets"/>, so a model plugin reads the game
/// through the host's one shared mount instead of racing it with a second one (see
/// <see cref="GameDataGate"/>).
/// </remarks>
public sealed class SceneModelHostService
{
    private readonly HostSettingsService _settings;
    private readonly ConcurrentDictionary<string, SceneClassModel?> _classes = new(StringComparer.Ordinal);

    public SceneModelHostService(HostSettingsService settings)
    {
        _settings = settings;
        PluginHostEnvironment.GameAssets = static () =>
        {
            try { return GameDataGate.CreateProvider(GameDataLanguageStore.Saved); }
            catch { return null; }
        };
    }

    /// <summary>The first loaded provider that reports itself available, or null.</summary>
    public PluginCapability<ISceneModelProvider>? Provider
    {
        get
        {
            _settings.EnsurePluginsLoaded();
            foreach (var capability in PluginManager.Shared.SceneModelProviders)
            {
                try
                {
                    if (capability.Value.IsAvailable) return capability;
                }
                catch (Exception ex)
                {
                    EditorLog.Warn("Scene", $"3D model provider '{capability.Value.Id}' failed its availability check: {ex.Message}");
                }
            }
            return null;
        }
    }

    /// <summary>What the view shows in its status line.</summary>
    public SceneModelStatus Status()
    {
        var provider = Provider;
        var installed = PluginManager.Shared.SceneModelProviders.Count > 0;
        return provider is null
            ? new SceneModelStatus(false, installed, null, null)
            : new SceneModelStatus(true, true, provider.Value.Title, provider.Plugin.Manifest.Name);
    }

    /// <summary>
    /// Describes each class (null for one the provider has no model for). Answers are cached for
    /// the process: a class's model only changes with a game update, which restarts the editor.
    /// </summary>
    public IReadOnlyDictionary<string, SceneClassModel?> DescribeClasses(IEnumerable<string> classPaths)
    {
        var provider = Provider?.Value;
        var paths = classPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Take(2000).ToList();
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, SceneClassModel?>(StringComparer.Ordinal);
        // Each class is mostly a small cache file read (or, the first time, a game-file read the
        // provider serializes itself), so a batch is worked out in parallel rather than one by one.
        Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount) }, path =>
        {
            if (provider is null) { result[path] = null; return; }
            result[path] = _classes.GetOrAdd(path, p =>
            {
                try { return ParseModelKey(p) is (var cls, { } state) ? provider.DescribeClass(cls, state) : provider.DescribeClass(p); }
                catch (Exception ex)
                {
                    EditorLog.Warn("Scene", $"No 3D model for {p}: {ex.Message}");
                    return null;
                }
            });
        });
        return new Dictionary<string, SceneClassModel?>(result, StringComparer.Ordinal);
    }

    /// <summary>
    /// The viewer asks for an object's model as its class path followed by the parts of its state
    /// that change how it looks: <c>#paint=&lt;EPaintColor value&gt;</c> and
    /// <c>#crops=&lt;spot&gt;.&lt;crop row&gt;.&lt;stage&gt;,...</c> and <c>#liquid=&lt;level&gt;</c>. A plain class path has no state, and a
    /// key with anything else after the class path is passed on unchanged.
    /// </summary>
    public static (string ClassPath, SceneObjectState? State) ParseModelKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var at = key.IndexOf('#', StringComparison.Ordinal);
        if (at <= 0) return (key, null);
        int? paint = null;
        int? liquid = null;
        string? fluid = null;
        List<SceneCrop>? crops = null;
        foreach (var part in key[(at + 1)..].Split('#', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith(PaintPart, StringComparison.Ordinal) && TryNumber(part[PaintPart.Length..], out var p))
            {
                paint = p;
            }
            else if (part.StartsWith(LiquidPart, StringComparison.Ordinal) && TryNumber(part[LiquidPart.Length..], out var l))
            {
                liquid = l;
            }
            else if (part.StartsWith(FluidPart, StringComparison.Ordinal) && part[FluidPart.Length..] is { Length: > 0 and <= 80 } name
                     && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                fluid = name;
            }
            else if (part.StartsWith(CropsPart, StringComparison.Ordinal))
            {
                foreach (var spot in part[CropsPart.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = spot.Split('.');
                    if (bits.Length != 3 || !TryNumber(bits[0], out var index) || !TryNumber(bits[2], out var stage)
                        || bits[1].Length is 0 or > 80 || !bits[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    {
                        return (key, null);
                    }
                    (crops ??= []).Add(new SceneCrop(index, bits[1], stage));
                }
            }
            else
            {
                return (key, null);
            }
        }
        return (key[..at], new SceneObjectState(paint, crops, liquid, fluid));
    }

    private static bool TryNumber(string text, out int value)
        => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>Paint part of a model key (see <see cref="ParseModelKey"/>).</summary>
    public const string PaintPart = "paint=";

    /// <summary>Crops part of a model key (see <see cref="ParseModelKey"/>).</summary>
    public const string CropsPart = "crops=";

    /// <summary>Liquid part of a model key: how much a liquid container holds (see <see cref="ParseModelKey"/>).</summary>
    public const string LiquidPart = "liquid=";

    /// <summary>Fluid part of a model key: which liquid, as its enum value name (see <see cref="ParseModelKey"/>).</summary>
    public const string FluidPart = "fluid=";

    /// <summary>The level geometry around a base, or null when the provider does not draw levels.</summary>
    public SceneLevelSlice? DescribeLevel(SceneLevelQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var provider = Provider?.Value;
        if (provider is null || string.IsNullOrWhiteSpace(query.Region)) return null;
        if (query.Min is not { Length: 3 } || query.Max is not { Length: 3 }) return null;
        var capped = query with { MaxInstances = Math.Clamp(query.MaxInstances, 1, 200_000) };
        try { return provider.DescribeLevel(capped); }
        catch (Exception ex)
        {
            EditorLog.Warn("Scene", $"Level geometry for {query.Region} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>A mesh or texture by the id a model referenced, or null.</summary>
    public SceneAsset? OpenAsset(string assetId)
    {
        var provider = Provider?.Value;
        if (provider is null || string.IsNullOrWhiteSpace(assetId) || assetId.Length > 512) return null;
        try { return provider.OpenAsset(assetId); }
        catch (Exception ex)
        {
            EditorLog.Warn("Scene", $"3D asset {assetId} failed: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Whether game models are on offer, and from which plugin.</summary>
/// <param name="Available">A provider is loaded and can read the game.</param>
/// <param name="Installed">A provider plugin is loaded at all (it may still lack a game install).</param>
/// <param name="Title">The provider's own name.</param>
/// <param name="Plugin">The plugin that supplies it.</param>
public sealed record SceneModelStatus(bool Available, bool Installed, string? Title, string? Plugin);
