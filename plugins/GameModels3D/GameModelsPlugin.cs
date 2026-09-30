using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Entry point: registers the model provider. All reading happens lazily, when the 3D view first
/// asks for a model, so installing the plugin costs nothing until that tab is opened.
/// </summary>
public sealed class GameModelsPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(host);
        registry.AddSceneModelProvider(new PakSceneModelProvider(host));
    }
}
