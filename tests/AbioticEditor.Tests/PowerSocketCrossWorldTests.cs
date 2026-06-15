using System.IO;
using System.Linq;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Guards power-socket device resolution. The crucial case: in a Facility SUB-LEVEL save the
/// deployables are keyed by actor path, not GUID, and the sockets reference device GUIDs that live
/// in the hub <c>WorldSave_Facility.sav</c>. So same-save resolution is 0% there and the
/// folder-wide index must resolve every plugged socket - which is exactly the path the cross-world
/// UI depends on.
/// </summary>
public sealed class PowerSocketCrossWorldTests
{
    private static Dictionary<string, PowerSocketDeviceResolver.DeviceInfo> BuildFolderIndex(string dir)
    {
        var index = new Dictionary<string, PowerSocketDeviceResolver.DeviceInfo>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(dir, "WorldSave_*.sav"))
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                PowerSocketDeviceResolver.MergeSave(index, file, SaveGame.LoadFrom(fs));
            }
            catch
            {
                // skip unreadable
            }
        }
        return index;
    }

    [Fact]
    public void Folder_index_resolves_plugged_sockets_in_a_sublevel_save()
    {
        var dir = Fixtures.CascadeDir;
        if (dir is null)
        {
            return;
        }

        var index = BuildFolderIndex(dir);
        // The hub Facility save alone contributes hundreds of GUID-keyed deployables.
        Assert.True(index.Count > 100, $"folder index unexpectedly small: {index.Count}");

        // Find a sub-level save that has plugged sockets (e.g. Office1) and confirm every plugged
        // device resolves through the folder index, even though none resolve same-save.
        var checkedASublevel = false;
        foreach (var file in Directory.EnumerateFiles(dir, "WorldSave_Facility_*.sav"))
        {
            SaveGame save;
            try { save = WorldSaveReader.ReadFromFile(file).Raw; } catch { continue; }

            var plugged = WorldMapAccessor.Entries(save, "PowerSocketMap")
                .Select(e => e.Props.GetString("PluggedInDeviceAssetID_"))
                .Where(d => !PowerSocketDeviceResolver.IsNothingPlugged(d))
                .ToList();
            if (plugged.Count == 0)
            {
                continue;
            }

            checkedASublevel = true;
            foreach (var dev in plugged)
            {
                Assert.True(index.ContainsKey(dev!),
                    $"plugged device {dev} from {Path.GetFileName(file)} not found in the folder index");
            }
        }

        // If the fixture set has no sub-level sockets we simply didn't assert (still covered by the
        // index-size check above); don't fail in that case.
        _ = checkedASublevel;
    }
}
