using System.Runtime.InteropServices;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// CUE4Parse's optional native library (<c>CUE4Parse-Natives</c>), which decodes animations compressed
/// with ACL. The editor does not ship it; the plugin's download carries it next to the plugin, and it is
/// loaded from there before CUE4Parse first asks for it by name, so that lookup finds the loaded module.
/// Without it (a build from source, a platform it was not built for) posed meshes whose animation uses
/// ACL keep their rest pose.
/// </summary>
internal static class NativeDecoder
{
    private static readonly string[] FileNames = ["CUE4Parse-Natives.dll", "CUE4Parse-Natives.so", "libCUE4Parse-Natives.so", "CUE4Parse-Natives.dylib"];

    /// <summary>True once the library is loaded (from <paramref name="folder"/>, or already in the process).</summary>
    public static bool Loaded { get; private set; }

    /// <summary>Loads the library from <paramref name="folder"/> when it is there. Never throws.</summary>
    public static bool TryLoad(string? folder)
    {
        if (Loaded) return true;
        if (string.IsNullOrEmpty(folder)) return false;
        foreach (var name in FileNames)
        {
            var path = Path.Combine(folder, name);
            if (!File.Exists(path)) continue;
            if (NativeLibrary.TryLoad(path, out var handle))
            {
                Loaded = true;
                // Windows finds a module already loaded under the same name. Elsewhere a lookup by
                // name does not, so CUE4Parse is pointed at this handle (once; another resolver wins).
                if (!OperatingSystem.IsWindows()) PointCue4ParseAt(handle);
                return true;
            }
        }
        return false;
    }

    private static void PointCue4ParseAt(IntPtr handle)
    {
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(CUE4Parse.UE4.Assets.Exports.Animation.UAnimSequence).Assembly,
                (name, _, _) => name.Contains("CUE4Parse-Natives", StringComparison.Ordinal) ? handle : IntPtr.Zero);
        }
        catch (InvalidOperationException)
        {
            // A resolver is already set for CUE4Parse (the host's own); leave it.
        }
    }
}
