using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using UeSaveGame;

namespace AbioticEditor.Tests.Features;

/// <summary>
/// Picks the Facility region save the world-map feature tests run against, deliberately rather
/// than by whatever order the filesystem hands back.
/// </summary>
/// <remarks>
/// The client fixture holds several worlds, and only one of them was played far enough to contain
/// player-built structures such as teleporter pads. Taking "the first WorldSave_Facility.sav found"
/// therefore chose a different world on Linux (ext4 hands back directory entries in hash order)
/// than on Windows (NTFS hands them back sorted), so a suite that was green on every developer
/// machine went red on CI with an empty pad list. The world is now chosen by what it contains.
/// </remarks>
internal static class FacilityFixture
{
    public const string SkipReason = "the client save fixture is not in this checkout";

    /// <summary>Resolved once for the whole suite: locating it parses the candidate saves.</summary>
    private static readonly Lazy<string?> Resolved = new(Locate);

    /// <summary>Every live Facility save in the fixture, in a stable order.</summary>
    public static IReadOnlyList<string> Candidates => Fixtures.ClientWorldSaves("WorldSave_Facility.sav");

    /// <summary>The chosen save's path, or null when no fixture world carries built structures.</summary>
    public static string? Path => Resolved.Value;

    private static string? Locate()
    {
        // Teleporter pads are the marker for "a world someone actually built in": a world with
        // pads also carries the sockets, deployables and containers the other feature tests want.
        var pads = WorldMapFeatures.Find("teleporter-pads")!;
        foreach (var path in Candidates)
        {
            if (pads.AppliesTo(WorldSaveReader.ReadFromFile(path).Raw))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// A freshly parsed copy of that save (tests mutate what they load, so they cannot share one).
    /// Skips when the fixture is absent from the checkout, and fails loudly when it is present but
    /// no world carries pads: that means the reader stopped seeing them, which is a real defect.
    /// </summary>
    public static SaveGame Load()
    {
        Skip.IfNot(Candidates.Count > 0, SkipReason);
        Assert.True(Path is not null,
            "no Facility region save in the client fixture carries placed teleporter pads: "
            + string.Join(", ", Candidates));
        return WorldSaveReader.ReadFromFile(Path!).Raw;
    }
}
