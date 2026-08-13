using AbioticEditor.Core.GamePass;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// What the editor is allowed to offer a player about a Game Pass save, and when.
/// </summary>
/// <remarks>
/// The rule these pin down is that the editor only offers a repair when a repair would actually
/// change something. Offering one because a save merely looks unwell led to a prompt that reported
/// fixing nothing, which is how people learn to click past prompts. Some of what makes a Game Pass
/// save look unwell (an unsettled cloud conflict above all) is not something this editor can put
/// right at all, and saying so is more use than offering a button that will not help.
/// </remarks>
public sealed class GamePassRecoveryServiceTests
{
    [Fact]
    public void A_workspace_with_no_game_pass_save_is_offered_no_repair()
    {
        using var workspace = NewWorkspace();

        // Nothing is open, so there is no folder to look in. Asking anyway must answer "no" rather
        // than throw: the save screen asks on every workspace, Game Pass or not.
        Assert.False(GamePassRecovery.RepairIsTheRemedy(workspace));
        Assert.Null(workspace.GamePassWriteState());
    }

    [SkippableFact]
    public void A_healthy_save_needs_no_repair_so_nothing_is_offered()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-repair-healthy-");
        var wgs = BuildGamePassWorld(scratch.Path, "MyWorld");

        // The freshly written save is consistent, so the player must not be asked about a repair
        // before it opens.
        Assert.Empty(GamePassSaveSet.PartsNeedingRepair(wgs));
    }

    [SkippableFact]
    public void A_save_left_in_a_state_the_format_does_not_define_is_offered_a_repair()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-repair-broken-");
        var wgs = BuildGamePassWorld(scratch.Path, "MyWorld");
        PatchFirstEntryState(wgs, 7);

        var needing = GamePassSaveSet.PartsNeedingRepair(wgs);
        Assert.NotEmpty(needing);

        // And the repair really does clear it, so the count the player is shown afterwards is not
        // zero. That mismatch, offered and then "fixed 0", is the thing this guards against.
        var repaired = GamePassSaveSet.Open(wgs).RepairMidSync();
        Assert.NotEmpty(repaired);
        Assert.Empty(GamePassSaveSet.PartsNeedingRepair(wgs));
    }

    [SkippableFact]
    public void A_stale_cloud_conflict_is_offered_as_a_repair_and_really_clears()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-repair-conflict-");
        var wgs = BuildGamePassWorld(scratch.Path, "MyWorld");
        SetUnresolvedConflict(wgs);

        // The editor used to leave this marker strictly alone, because only the service that set it
        // can know the conflict is over. On a real save that produced a world nobody could ever edit
        // again: the marker was observed set continuously for seven weeks, across many play
        // sessions, blocking every write, with nothing in existence that would take it off.
        Assert.True(WgsContainerStore.Open(wgs).HasUnresolvedConflicts);
        Assert.NotEmpty(GamePassSaveSet.PartsNeedingRepair(wgs));

        var repaired = GamePassSaveSet.Open(wgs).RepairMidSync();
        Assert.NotEmpty(repaired);

        var after = WgsContainerStore.Open(wgs);
        Assert.False(after.HasUnresolvedConflicts);
        Assert.True(after.CheckWritable().CanWrite);
        Assert.Empty(GamePassSaveSet.PartsNeedingRepair(wgs));
    }

    [SkippableFact]
    public void Clearing_a_stale_conflict_does_not_touch_the_save_data()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-repair-conflict-data-");
        var wgs = BuildGamePassWorld(scratch.Path, "MyWorld");
        SetUnresolvedConflict(wgs);

        var before = BlobHashes(wgs);
        GamePassSaveSet.Open(wgs).RepairMidSync();

        // The marker lives in the folder's contents list, not in a world. Clearing it must leave
        // every byte of every save exactly where it was, or a repair is a gamble rather than a fix.
        Assert.Equal(before, BlobHashes(wgs));
    }

    /// <summary>Hashes of every data blob in the folder, so a repair can be shown to leave the
    /// saves themselves untouched.</summary>
    private static HashSet<string> BlobHashes(string wgsFolder)
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(wgsFolder, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.Length != 32) continue;
            hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
        }
        return hashes;
    }

    [SkippableFact]
    public void A_world_with_no_difficulty_settings_stored_does_not_produce_an_empty_file()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-empty-ini-");
        var wgs = BuildGamePassWorld(scratch.Path, "MyWorld");
        AddEmptySandboxSettings(wgs, "MyWorld-WC", "MyWorld");

        var working = Path.Combine(scratch.Path, "working");
        GamePassSaveSet.Open(wgs).ExtractWorld("MyWorld-WC", working);

        // The game stores an empty settings member for a world whose difficulty was never changed.
        // Writing that out gave the editor a nought-byte file to offer, and opening it showed a
        // blank screen with no explanation.
        Assert.False(File.Exists(Path.Combine(working, "SandboxSettings.ini")));
        Assert.NotEmpty(Directory.GetFiles(working, "WorldSave_*.sav"));
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static SaveWorkspaceSessionService NewWorkspace()
        => new(new RecipeVocabularyService(), new ItemUpgradeVocabularyService(),
            new ProgressionVocabularyService(), new CodexVocabularyService(), new DesktopSaveFileSystem());

    /// <summary>A one-world Game Pass folder built out of the Steam fixture, so no personal Xbox
    /// data has to live in the repository.</summary>
    private static string BuildGamePassWorld(string root, string world)
    {
        var steam = Path.Combine(root, world);
        Directory.CreateDirectory(Path.Combine(steam, "PlayerData"));
        File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"),
            Path.Combine(steam, "WorldSave_MetaData.sav"));
        var player = Directory.EnumerateFiles(
                Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav")
            .OrderBy(p => p, StringComparer.Ordinal)
            .First();
        File.Copy(player, Path.Combine(steam, "PlayerData", Path.GetFileName(player)));
        return GamePassConverter.SteamWorldToGamePass(steam, Path.Combine(root, "wgs"), worldName: world);
    }

    /// <summary>Repacks the world with an empty settings member, which is what the game writes for
    /// a world whose difficulty was never changed.</summary>
    private static void AddEmptySandboxSettings(string wgsFolder, string container, string world)
    {
        var store = WgsContainerStore.Open(wgsFolder);
        var entry = store.Find(container)!;
        var bundle = AbfSaveBundle.Parse(store.ReadBlob(entry));
        var members = bundle.Members.ToList();
        members.Add(new AbfMember
        {
            Path = $"Profile/Worlds/{world}/SandboxSettings.ini",
            SaveClass = string.Empty,
            Flag = AbfMember.IniFlag,
            Body = [],
        });
        store.WriteBlob(entry, AbfSaveBundle.Create(members).Serialize());
    }

    private static void SetUnresolvedConflict(string wgsFolder)
        => PatchIndex(wgsFolder, (d, pos) =>
        {
            var flags = BitConverter.ToUInt32(d, pos);
            BitConverter.GetBytes(flags | 16u).CopyTo(d, pos);
        }, headerOnly: true);

    private static void PatchFirstEntryState(string wgsFolder, uint state)
        => PatchIndex(wgsFolder, (d, pos) => BitConverter.GetBytes(state).CopyTo(d, pos), headerOnly: false);

    /// <summary>
    /// Edits <c>containers.index</c> in place. The store will not write these shapes any more, so a
    /// save in one has to be built by hand rather than through a production API.
    /// </summary>
    private static void PatchIndex(string wgsFolder, Action<byte[], int> edit, bool headerOnly)
    {
        var path = Path.Combine(wgsFolder, "containers.index");
        var d = File.ReadAllBytes(path);
        var pos = 12;                        // version + count + reserved
        SkipWideString(d, ref pos);          // package family name
        pos += 8;                            // index FILETIME
        if (headerOnly) { edit(d, pos); File.WriteAllBytes(path, d); return; }

        pos += 4;                            // sync flags
        SkipWideString(d, ref pos);          // root GUID
        pos += 8;                            // reserved
        SkipWideString(d, ref pos);          // entry name
        SkipWideString(d, ref pos);          // entry name again
        SkipWideString(d, ref pos);          // etag
        pos += 1;                            // container number
        edit(d, pos);
        File.WriteAllBytes(path, d);
    }

    private static void SkipWideString(byte[] d, ref int pos)
        => pos += 4 + ((int)BitConverter.ToUInt32(d, pos) * 2);

    private sealed class TempCopy : IDisposable
    {
        private readonly DirectoryInfo _dir;

        private TempCopy(DirectoryInfo dir) => _dir = dir;

        public string Path => _dir.FullName;

        public static TempCopy Empty(string prefix) => new(Directory.CreateTempSubdirectory(prefix));

        public void Dispose()
        {
            try { _dir.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
