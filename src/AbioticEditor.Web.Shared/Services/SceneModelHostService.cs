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
        var result = new Dictionary<string, SceneClassModel?>(StringComparer.Ordinal);
        foreach (var path in classPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Take(2000))
        {
            if (provider is null) { result[path] = null; continue; }
            result[path] = _classes.GetOrAdd(path, p =>
            {
                try { return provider.DescribeClass(p); }
                catch (Exception ex)
                {
                    EditorLog.Warn("Scene", $"No 3D model for {p}: {ex.Message}");
                    return null;
                }
            });
        }
        return result;
    }

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
