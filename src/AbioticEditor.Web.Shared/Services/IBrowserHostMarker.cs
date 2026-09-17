namespace AbioticEditor.Web.Services;

/// <summary>
/// Marker registered only by the browser (WebAssembly) host's <c>Program.cs</c>. Its mere
/// presence in the service container is what shared screens use to tell a browser tab apart
/// from the desktop host, for features that must exist only in the web deployment - hiding
/// "move items between worlds" is the first one.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ILiveEditingCapability"/>'s own presence-means-something idiom, just for
/// the opposite host: components resolve it through <see cref="IServiceProvider.GetService"/>
/// rather than a required <c>[Inject]</c>, so nothing throws on the desktop host where it is
/// never registered. Kept separate from <see cref="IModDisclaimerGate"/>, which already carries
/// its own per-host behaviour (a no-op on desktop) and needs no separate "am I browser" check.
/// </remarks>
public interface IBrowserHostMarker
{
}

/// <summary>The web (WebAssembly) host's registration.</summary>
public sealed class BrowserHostMarker : IBrowserHostMarker
{
}
