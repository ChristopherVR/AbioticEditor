namespace AbioticEditor.Web.Services;

/// <summary>
/// Marker for whether this host offers live in-game editing. Registered only by the desktop
/// host's <c>Program.cs</c> - the WASM host never registers it, so
/// <c>ModeSelect.razor</c> resolves it through <see cref="IServiceProvider.GetService"/> rather
/// than a required <c>[Inject]</c>, and skips straight to the file-editing flow when it is
/// absent instead of throwing. This is the entire mechanism that keeps live editing out of the
/// browser build with no `#if`/conditional-compile split anywhere in the shared screens.
/// </summary>
public interface ILiveEditingCapability
{
    bool IsAvailable { get; }
}

/// <summary>The desktop host's registration: live editing is always offered there.</summary>
public sealed class DesktopLiveEditingCapability : ILiveEditingCapability
{
    public bool IsAvailable => true;
}
