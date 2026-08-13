using System.Text;
using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Tests;

/// <summary>
/// Game Pass / Xbox container support. These build a synthetic wgs + ABF_SAVE_VERSION layout from
/// a real Steam fixture (no personal Game Pass data committed) and round-trip it. The Oodle-backed
/// bundle tests skip gracefully when no native Oodle library is available (e.g. offline CI).
/// </summary>
public class GamePassTests
{
    private const string CharClass = GamePassMemberCodec.CharacterSaveClass;

    private static byte[] FixturePlayer()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var path = Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav");
        Assert.True(File.Exists(path), $"missing fixture: {path}");
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// The fixture world's player file names, in a fixed order. Directory enumeration hands them
    /// back in whatever order the filesystem keeps them, which differs between Windows and Linux,
    /// so a test that took "the first one" was quietly a different test on each platform.
    /// </summary>
    private static List<string> FixturePlayerNames()
        => Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void MemberCodec_strips_and_restores_header_losslessly()
    {
        var save = FixturePlayer();

        // A full Steam save is GVAS header + the same body a Game Pass member stores.
        var body = GamePassMemberCodec.ToMemberBody(CharClass, save);
        var rebuilt = GamePassMemberCodec.ToGvas(CharClass, body);

        // The body splits off cleanly: reconstruct -> parse -> re-serialize -> body is unchanged.
        var data = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(rebuilt)));
        using var ms = new MemoryStream();
        data.Raw.WriteTo(ms);
        var reBody = GamePassMemberCodec.ToMemberBody(CharClass, ms.ToArray());
        Assert.Equal(body, reBody);
    }

    /// <summary>
    /// Establishes what the length in a save's header actually counts, against real saves of all
    /// three kinds, so the reconstruction tests below are checking the game's rule rather than our
    /// own reading of it.
    /// </summary>
    [SkippableTheory]
    [InlineData("PlayerData/Player_76561197993781479.sav")]
    [InlineData("WorldSave_H_Cabin.sav")]
    [InlineData("WorldSave_MetaData.sav")]
    public void A_real_save_declares_the_bytes_that_follow_its_header(string relative)
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");

        var path = Path.Combine(Fixtures.CascadeDir!, relative.Replace('/', Path.DirectorySeparatorChar));
        Skip.IfNot(File.Exists(path), $"the fixture {relative} is not in this checkout");

        var (declared, actual) = GvasCustomHeader.Measure(File.ReadAllBytes(path));
        Assert.Equal(actual, declared);
    }

    /// <summary>
    /// The header template is captured from one real save, so it arrives carrying that save's
    /// length. Splicing it onto a different body unchanged produced a save that opened in the
    /// editor (which ignores the field) and was refused in-game as an incompatible world save.
    /// </summary>
    [Theory]
    [InlineData(GamePassMemberCodec.CharacterSaveClass)]
    [InlineData(GamePassMemberCodec.WorldSaveClass)]
    [InlineData(GamePassMemberCodec.WorldMetadataSaveClass)]
    public void A_reconstructed_save_declares_its_own_length(string saveClass)
    {
        // Rebuild several bodies in a row: the templates are shared statics, so a fix that stamped
        // the length into the template itself would pass on the first body and poison the rest.
        foreach (var size in new[] { 4096, 1, 70_000, 4096 })
        {
            var body = new byte[size];
            var gvas = GamePassMemberCodec.ToGvas(saveClass, body);

            var (declared, actual) = GvasCustomHeader.Measure(gvas);
            Assert.Equal(size, actual);
            Assert.Equal(size, declared);
        }
    }

    /// <summary>
    /// A Game Pass member is only the save's body, so the format numbers in front of the length
    /// (a character save's version; a world save's version and id) can only come from the captured
    /// template - nothing in the bundle carries them per save. They are the same in every real save
    /// we have, across game versions, so the template's copies are right; this is what notices if a
    /// game update ever moves them and the templates need recapturing.
    /// </summary>
    [SkippableTheory]
    [InlineData(GamePassMemberCodec.CharacterSaveClass, "PlayerData/Player_76561197993781479.sav")]
    [InlineData(GamePassMemberCodec.WorldSaveClass, "WorldSave_H_Cabin.sav")]
    [InlineData(GamePassMemberCodec.WorldMetadataSaveClass, "WorldSave_MetaData.sav")]
    public void A_reconstructed_save_carries_the_format_numbers_a_real_save_carries(string saveClass, string relative)
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");

        var path = Path.Combine(Fixtures.CascadeDir!, relative.Replace('/', Path.DirectorySeparatorChar));
        Skip.IfNot(File.Exists(path), $"the fixture {relative} is not in this checkout");

        Assert.Equal(
            GvasCustomHeader.Versions(File.ReadAllBytes(path)),
            GvasCustomHeader.Versions(GamePassMemberCodec.ToGvas(saveClass, new byte[64])));
    }

    [SkippableFact]
    public void Every_save_taken_out_of_a_real_Game_Pass_world_declares_its_own_length()
    {
        Skip.IfNot(Fixtures.GamePassWgsDir is not null, "the Game Pass fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var work = Directory.CreateTempSubdirectory("gp-extract-lengths");
        try
        {
            var set = GamePassSaveSet.Open(Fixtures.GamePassWgsDir!);
            var container = set.Entries().Select(e => e.ContainerName).Distinct(StringComparer.OrdinalIgnoreCase).First();
            set.ExtractWorld(container, work.FullName);

            var saves = Directory.GetFiles(work.FullName, "*.sav", SearchOption.AllDirectories);
            Assert.NotEmpty(saves);
            foreach (var save in saves)
            {
                var (declared, actual) = GvasCustomHeader.Measure(File.ReadAllBytes(save));
                Assert.Equal(actual, declared);
            }
        }
        finally
        {
            work.Delete(recursive: true);
        }
    }

    [Fact]
    public void WgsContainerStore_writes_and_reads_a_blob()
    {
        var dir = Directory.CreateTempSubdirectory("wgs-test");
        try
        {
            BuildSyntheticWgs(dir.FullName, "ForScience-WC", new byte[] { 1, 2, 3, 4, 5 });
            var store = WgsContainerStore.Open(dir.FullName);
            var c = store.Find("ForScience-WC");
            Assert.NotNull(c);
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, store.ReadBlob(c!));

            // Write a new, larger blob; a fresh store must read it back and bump the generation.
            var oldNum = c!.ContainerNumber;
            store.WriteBlob(c, new byte[] { 9, 9, 9, 9, 9, 9, 9 });
            var reopened = WgsContainerStore.Open(dir.FullName);
            var c2 = reopened.Find("ForScience-WC")!;
            Assert.Equal(7, c2.BlobSize);
            Assert.Equal(unchecked((byte)(oldNum + 1)), c2.ContainerNumber);
            Assert.Equal(new byte[] { 9, 9, 9, 9, 9, 9, 9 }, reopened.ReadBlob(c2));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveContainerFolder_finds_the_index_from_any_nearby_level()
    {
        var parent = Directory.CreateTempSubdirectory("wgs-resolve");
        try
        {
            // The container folder (holds containers.index) sits one level under the picked parent,
            // mirroring a real "<...>\wgs\<account>" tree where <account> holds the index.
            var account = Path.Combine(parent.FullName, "00090000_ABCDEF");
            Directory.CreateDirectory(account);
            BuildSyntheticWgs(account, "W-WC", new byte[] { 1, 2, 3 });
            var blobSubfolder = Directory.EnumerateDirectories(account)
                .First(d => WgsContainerStore.IsContainerFolder(d) == false);

            // Picked the container folder itself.
            Assert.Equal(account, WgsContainerStore.ResolveContainerFolder(account));
            // Picked the parent ("wgs"): a child is the container folder.
            Assert.Equal(account, WgsContainerStore.ResolveContainerFolder(parent.FullName));
            // Picked a GUID blob sub-folder: its parent is the container folder.
            Assert.Equal(account, WgsContainerStore.ResolveContainerFolder(blobSubfolder));
            // An unrelated folder resolves to nothing.
            var unrelated = Path.Combine(parent.FullName, "nope");
            Directory.CreateDirectory(unrelated);
            Assert.Null(WgsContainerStore.ResolveContainerFolder(unrelated));
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void AbfBundle_round_trips_through_oodle()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var body = GamePassMemberCodec.ToMemberBody(CharClass, FixturePlayer());
        var bundle = TestBundle(("Profile/Worlds/W/PlayerData/Player_1", CharClass, body));

        var blob = bundle.Serialize();
        Assert.True(AbfSaveBundle.LooksLikeBundle(blob));
        var reparsed = AbfSaveBundle.Parse(blob);

        Assert.Single(reparsed.Members);
        Assert.Equal(body, reparsed.Members[0].Body);
        Assert.Equal(CharClass, reparsed.Members[0].SaveClass);
    }

    /// <summary>
    /// The game passes Field1 from the bundle header verbatim to OodleLZ_Decompress as rawLen;
    /// Field1 must equal the actual total decompressed size after member edits. This test verifies
    /// that Serialize() writes Field1 = sum(member.Body.Length) even when a member grows.
    /// </summary>
    [SkippableFact]
    public void AbfBundle_serialize_updates_field1_to_match_total_body_size()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var smallBody = new byte[] { 1, 2, 3, 4 };
        var bundle = TestBundle(("Profile/Worlds/W/PlayerData/Player_1", CharClass, smallBody));

        // Grow the member body to simulate an edit that increases the save size.
        var largerBody = new byte[660738];
        new Random(42).NextBytes(largerBody);
        bundle.Members[0].Body = largerBody;

        var blob = bundle.Serialize();
        var reparsed = AbfSaveBundle.Parse(blob);

        // Field1 in the serialized blob must equal the new total, not the old one.
        Assert.Equal((uint)largerBody.Length, reparsed.Field1);
        Assert.Equal(largerBody, reparsed.Members[0].Body);
    }

    [SkippableFact]
    public void OodleCompress_roundtrips_large_payload()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        const int Size = 660738; // same size as the real-world failure case
        var original = new byte[Size];
        for (var i = 0; i < Size; i++) original[i] = (byte)(i * 7 % 251);

        var compressed = OodleCodec.Compress(original);
        var decompressed = OodleCodec.Decompress(compressed, Size);
        Assert.Equal(Size, decompressed.Length);
        Assert.Equal(original, decompressed);
    }

    [SkippableTheory]
    [InlineData(524287)]
    [InlineData(524288)]
    [InlineData(524289)]
    [InlineData(700000)]
    public void OodleCompress_roundtrips_sizes_around_512KB_boundary(int size)
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var original = new byte[size];
        for (var i = 0; i < size; i++) original[i] = (byte)(i * 13 % 251);

        var compressed = OodleCodec.Compress(original);
        var decompressed = OodleCodec.Decompress(compressed, size);

        Assert.Equal(size, decompressed.Length);
        Assert.Equal(original, decompressed);
    }

    [SkippableFact]
    public void GamePassSaveSet_edits_a_packed_player_end_to_end()
    {
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var dir = Directory.CreateTempSubdirectory("gp-set");
        try
        {
            // Pack a real player into a synthetic Game Pass world container.
            var body = GamePassMemberCodec.ToMemberBody(CharClass, FixturePlayer());
            var bundle = TestBundle(("Profile/Worlds/W/PlayerData/Player_2533274900397709", CharClass, body));
            BuildSyntheticWgs(dir.FullName, "W-WC", bundle.Serialize());

            var set = GamePassSaveSet.Open(dir.FullName);
            var entry = set.Entries().Single(e => e.Kind == GamePassSaveKind.Player);
            Assert.Equal("Player_2533274900397709.sav", entry.FileName);

            // Read -> edit money via the real reader/writer -> write back.
            var data = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(set.ReadSave(entry))));
            PlayerSaveWriter.ApplyStats(data, data.Stats with { Money = 123456 });
            using var ms = new MemoryStream();
            data.Raw.WriteTo(ms);
            set.WriteSave(entry, ms.ToArray());

            // Reopen the container from disk; the edit must be there and everything else intact.
            var reopened = GamePassSaveSet.Open(dir.FullName);
            var entry2 = reopened.Entries().Single(e => e.Kind == GamePassSaveKind.Player);
            var data2 = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(reopened.ReadSave(entry2))));
            Assert.Equal(123456, data2.Stats.Money);
            Assert.True(Directory.Exists(dir.FullName + ".bak"), "the wgs folder should be backed up on write");
        }
        finally
        {
            dir.Delete(recursive: true);
            if (Directory.Exists(dir.FullName + ".bak")) Directory.Delete(dir.FullName + ".bak", recursive: true);
        }
    }

    [SkippableFact]
    public void RealFixture_lists_reads_and_edits_a_packed_player()
    {
        Skip.IfNot(Fixtures.GamePassWgsDir is not null, "the Game Pass fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        // Work on a throwaway copy so the committed fixture is never mutated.
        var work = Directory.CreateTempSubdirectory("gp-fixture");
        try
        {
            CopyTree(Fixtures.GamePassWgsDir!, work.FullName);

            var set = GamePassSaveSet.Open(work.FullName);
            var entries = set.Entries();
            Assert.Contains(entries, e => e.Kind == GamePassSaveKind.Player);
            Assert.Contains(entries, e => e.Kind == GamePassSaveKind.WorldMetadata);

            var player = entries.First(e => e.Kind == GamePassSaveKind.Player);

            // The real Game Pass member reconstructs into a save the editor parses.
            var data = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(set.ReadSave(player))));
            Assert.NotEmpty(data.Skills);

            // Edit -> write back -> reopen from disk -> the edit survives the wgs/ABF/Oodle round-trip.
            PlayerSaveWriter.ApplyStats(data, data.Stats with { Money = 314159 });
            using var ms = new MemoryStream();
            data.Raw.WriteTo(ms);
            set.WriteSave(player, ms.ToArray());

            var reopened = GamePassSaveSet.Open(work.FullName);
            var p2 = reopened.Entries().First(e => e.Kind == GamePassSaveKind.Player);
            var data2 = PlayerSaveReader.ReadFrom(UeSaveGame.SaveGame.LoadFrom(new MemoryStream(reopened.ReadSave(p2))));
            Assert.Equal(314159, data2.Stats.Money);
        }
        finally
        {
            work.Delete(recursive: true);
            if (Directory.Exists(work.FullName + ".bak")) Directory.Delete(work.FullName + ".bak", recursive: true);
        }
    }

    private static void CopyTree(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, dest));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, dest), overwrite: true);
        }
    }

    [SkippableFact]
    public void Converts_Steam_world_to_GamePass_and_back_losslessly()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var tmp = Directory.CreateTempSubdirectory("steam-gp-convert");
        try
        {
            // A minimal Steam world: the metadata + every player in the Cascade fixture. All of them
            // matters: each save carries its own length in its header, so a conversion that hands
            // out one save's length to all of them still looks perfect on whichever player happens
            // to come first.
            var steam = Path.Combine(tmp.FullName, "MyWorld");
            Directory.CreateDirectory(Path.Combine(steam, "PlayerData"));
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"),
                Path.Combine(steam, "WorldSave_MetaData.sav"));
            var playerNames = FixturePlayerNames();
            Assert.True(playerNames.Count > 1, "the world fixture should hold several players");
            foreach (var name in playerNames)
            {
                File.Copy(Path.Combine(Fixtures.CascadeDir!, "PlayerData", name),
                    Path.Combine(steam, "PlayerData", name));
            }

            // Steam -> Game Pass.
            var wgs = GamePassConverter.SteamWorldToGamePass(steam, Path.Combine(tmp.FullName, "gp"));
            Assert.True(GamePassSaveSet.IsGamePassFolder(wgs));
            var set = GamePassSaveSet.Open(wgs);
            foreach (var name in playerNames)
            {
                Assert.Contains(set.Entries(), e => e.FileName == name);
            }

            // Game Pass -> Steam, into a new folder.
            var back = GamePassConverter.GamePassToSteamWorld(wgs, $"MyWorld-WC", Path.Combine(tmp.FullName, "back"));

            // Every save survives the round-trip byte-for-byte, not just the first one.
            foreach (var name in playerNames)
            {
                var restored = Path.Combine(back, "PlayerData", name);
                Assert.True(File.Exists(restored), $"{name} did not come back from the round-trip");
                Assert.Equal(File.ReadAllBytes(Path.Combine(steam, "PlayerData", name)), File.ReadAllBytes(restored));
            }
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(steam, "WorldSave_MetaData.sav")),
                File.ReadAllBytes(Path.Combine(back, "WorldSave_MetaData.sav")));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    [SkippableFact]
    public void Conversion_can_rehome_the_player_to_a_new_id()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        var tmp = Directory.CreateTempSubdirectory("gp-rehome");
        try
        {
            var steam = Path.Combine(tmp.FullName, "W");
            Directory.CreateDirectory(Path.Combine(steam, "PlayerData"));
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"),
                Path.Combine(steam, "WorldSave_MetaData.sav"));
            var srcPlayer = FixturePlayerNames()[0];
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "PlayerData", srcPlayer),
                Path.Combine(steam, "PlayerData", srcPlayer));

            const string newId = "msft-9Z8Y7X";
            var wgs = GamePassConverter.SteamWorldToGamePass(steam, Path.Combine(tmp.FullName, "gp"), worldName: "W", newPlayerId: newId);

            var set = GamePassSaveSet.Open(wgs);
            var player = set.Entries().Single(e => e.Kind == GamePassSaveKind.Player);
            Assert.Equal($"Player_{newId}.sav", player.FileName);
            // The SaveIdentifier inside the save was re-homed too.
            Assert.Equal(newId, AbioticEditor.Core.PlayerSaves.PlayerSaveIdentity.GetSaveIdentifier(
                UeSaveGame.SaveGame.LoadFrom(new MemoryStream(set.ReadSave(player)))));
        }
        finally
        {
            tmp.Delete(recursive: true);
        }
    }

    // ---- helpers: build a minimal but real wgs container folder + ABF bundle ----

    private static AbfSaveBundle TestBundle(params (string Path, string Class, byte[] Body)[] members)
    {
        // Re-create via Parse(Serialize(...)) is circular, so build the blob by hand-serializing a
        // bundle we construct through its own Serialize. We do that by faking a parse from a
        // minimal hand-built blob: simplest is to use reflection-free construction via Serialize of
        // a bundle assembled from a round-tripped empty. Instead, assemble the blob bytes directly.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        WriteStr(w, "ABF_SAVE_VERSION");
        w.Write(3);                       // version
        w.Write(0);                       // field1
        w.Write(16);                      // field2
        w.Write(members.Length);
        foreach (var m in members)
        {
            WriteStr(w, m.Path);
            w.Write(m.Body.Length);
            WriteStr(w, m.Class);
            w.Write(0);                   // flag
        }
        var raw = members.SelectMany(m => m.Body).ToArray();
        var comp = OodleCodec.Compress(raw);
        w.Write(1);                       // method = Oodle
        w.Write(comp.Length);
        w.Flush();
        ms.Write(comp, 0, comp.Length);
        return AbfSaveBundle.Parse(ms.ToArray());
    }

    private static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.ASCII.GetBytes(s);
        w.Write(b.Length + 1);
        w.Write(b);
        w.Write((byte)0);
    }

    private static void BuildSyntheticWgs(string root, string containerName, byte[] blob)
    {
        var folderGuid = Guid.NewGuid();
        var folderName = folderGuid.ToString("N").ToUpperInvariant();
        var folder = Path.Combine(root, folderName);
        Directory.CreateDirectory(folder);

        var blobGuid = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(folder, blobGuid.ToString("N").ToUpperInvariant()), blob);

        // container.1 manifest
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write(4u); w.Write(1u);
            var nameField = new byte[128];
            Encoding.Unicode.GetBytes("Data").CopyTo(nameField, 0);
            w.Write(nameField);
            w.Write(blobGuid.ToByteArray());
            w.Write(blobGuid.ToByteArray());
            File.WriteAllBytes(Path.Combine(folder, "container.1"), ms.ToArray());
        }

        // containers.index
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms, Encoding.Unicode))
        {
            w.Write(14u);                 // version
            w.Write(1u);                  // container count
            w.Write(0u);                  // reserved
            WriteWStr(w, "Synthetic.Abiotic_Test!App");
            w.Write(DateTime.UtcNow.ToFileTimeUtc());
            w.Write(3u);
            WriteWStr(w, Guid.NewGuid().ToString());
            w.Write(new byte[8]);         // 8 reserved bytes
            WriteWStr(w, containerName);
            WriteWStr(w, containerName);
            WriteWStr(w, "\"0x1\"");
            w.Write((byte)1);             // container number -> container.1
            w.Write(1u);                  // generation
            w.Write(folderGuid.ToByteArray());
            w.Write(DateTime.UtcNow.ToFileTimeUtc());
            w.Write(0L);
            w.Write((long)blob.Length);
            File.WriteAllBytes(Path.Combine(root, "containers.index"), ms.ToArray());
        }
    }

    private static void WriteWStr(BinaryWriter w, string s)
    {
        w.Write((uint)s.Length);
        w.Write(Encoding.Unicode.GetBytes(s));
    }
}

/// <summary>
/// Reads the two numbers that have to agree in any Abiotic Factor save: the length its custom
/// header declares, and the bytes that really follow that header. Measured straight out of the
/// bytes, by finding the save class name and skipping the class's fixed-size custom header, so a
/// test using it is not simply asking the production codec whether the production codec is right.
/// </summary>
internal static class GvasCustomHeader
{
    // Class name (with its FString null terminator) and the size of the custom header behind it:
    // a character save's is [int Version][int DataLength]; a world or metadata save's adds the
    // "ABF_SAVE_VERSION" marker and an id in front of the same length.
    private static readonly (string Marker, int CustomHeaderSize)[] Classes =
    [
        ("Abiotic_CharacterSave_C\0", 8),
        ("Abiotic_WorldSave_C\0", 33),
        ("Abiotic_WorldMetadataSave_C\0", 33),
    ];

    public static (int Declared, int Actual) Measure(byte[] gvas)
    {
        var (headerEnd, _) = Locate(gvas);
        return (BitConverter.ToInt32(gvas, headerEnd - sizeof(int)), gvas.Length - headerEnd);
    }

    /// <summary>
    /// The format numbers in front of the length: a character save's single version, or a world or
    /// metadata save's version and id (null for a character save, which has no id).
    /// </summary>
    public static (int Version, int? Id) Versions(byte[] gvas)
    {
        var (headerEnd, customHeaderSize) = Locate(gvas);
        var customHeader = headerEnd - customHeaderSize;
        return customHeaderSize == 8
            ? (BitConverter.ToInt32(gvas, customHeader), null)
            // Past the FString length and the 17 bytes of "ABF_SAVE_VERSION" plus its terminator.
            : (BitConverter.ToInt32(gvas, customHeader + 21), BitConverter.ToInt32(gvas, customHeader + 25));
    }

    private static (int HeaderEnd, int CustomHeaderSize) Locate(byte[] gvas)
    {
        foreach (var (marker, customHeaderSize) in Classes)
        {
            var markerBytes = Encoding.ASCII.GetBytes(marker);
            var idx = gvas.AsSpan().IndexOf(markerBytes);
            if (idx < 0) continue;

            return (idx + markerBytes.Length + customHeaderSize, customHeaderSize);
        }
        throw new InvalidDataException("These bytes are not an Abiotic Factor save of a known class.");
    }
}
