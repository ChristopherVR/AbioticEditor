using AbioticEditor.Web.Models;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The open world editor's "Show in 3D", for parts of the screen drawn outside the world tabs (the
/// right-hand detail panel shows a door's or a trader's card there). Null while no world editor with
/// a 3D view is open, which hides every "Show in 3D" button.
/// </summary>
public sealed class WorldLocatorService
{
    public WorldLocator? Current { get; set; }
}
