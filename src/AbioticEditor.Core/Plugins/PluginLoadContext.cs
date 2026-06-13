using System.Reflection;
using System.Runtime.Loader;

namespace AbioticEditor.Core.Plugins;

/// <summary>
/// An isolated <see cref="AssemblyLoadContext"/> for one plugin, so two plugins can ship
/// different versions of the same helper library without colliding (each resolves its own
/// copy from its own folder).
///
/// <para>
/// The context is deliberately <b>not</b> collectible: a managed plugin loads exactly once per
/// process and stays for its lifetime (enabling/disabling requires a restart), and its
/// capabilities are rooted by the descriptor anyway - so there is no unload path for
/// collectibility to serve, and a non-collectible context avoids the extra GC bookkeeping and
/// the JIT tiering limits collectible contexts impose. If hot unload-on-disable is added later,
/// flip this back to collectible and call <see cref="AssemblyLoadContext.Unload"/>.
/// </para>
///
/// <para>
/// The one thing that must NOT be isolated is the shared contract surface. If a plugin
/// loaded its own copy of the SDK (or UeSaveGame), its <c>ISaveOperation</c> would be a
/// different <see cref="Type"/> than the host's and every cast across the boundary would
/// fail. So <see cref="Load"/> deliberately returns null for those assemblies, which makes
/// the default (host) context provide them - giving one shared, identity-stable contract.
/// </para>
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>
    /// Assemblies that must come from the host, never a plugin-local copy, so types are
    /// identity-equal across the boundary. Anything in the default load context's TPA set
    /// (the .NET runtime + the host's own references) is already unified by returning null;
    /// these are the editor-specific contracts we additionally force to unify.
    /// </summary>
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "AbioticEditor.Plugins.Abstractions",
        "AbioticEditor.Core",
        "UeSaveGame",
        "UeSaveGame.Json",
    };

    public PluginLoadContext(string pluginId, string entryAssemblyPath)
        : base(name: $"plugin:{pluginId}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is { } name)
        {
            // 1. Always unify the editor contracts, even if not yet loaded - a plugin's
            //    ISaveOperation must be the host's type, or every cast fails.
            if (SharedAssemblies.Contains(name))
            {
                return null;
            }

            // 2. Unify ANYTHING the host already has loaded (CUE4Parse, System.*, etc.).
            //    Returning null falls back to the default context, giving one shared identity
            //    even if the plugin folder happens to carry its own copy of that DLL. Only
            //    genuinely plugin-private libraries fall through to step 3.
            if (IsLoadedInDefaultContext(name))
            {
                return null;
            }
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    private static bool IsLoadedInDefaultContext(string simpleName)
    {
        foreach (var assembly in Default.Assemblies)
        {
            if (string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
    }
}
