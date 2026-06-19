using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AbioticEditor.Core;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using UeSaveGame.Json;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

using AbioticEditor.Core.SaveClasses;

namespace AbioticEditor.Tests;

/// <summary>
/// Deep dive over the DEDICATED SERVER save tree
/// (<c>tests/fixtures/DedicatedServerSaves</c>) - a late-game world ("Cascade")
/// covering regions the checked-in fixture never reaches (Botanical, Fracture,
/// DarkFusion, DF_*, Labs_Adjustment, ...). Findings are written to
/// <c>docs/research-server-saves.md</c>.
///
/// All tests guard on the server tree being present and skip gracefully when it is not
/// (e.g. on CI), mirroring <see cref="NewSaveDeepDiveTests"/>.
/// </summary>
public class ServerSaveDeepDiveTests
{
    private readonly ITestOutputHelper _output;

    public ServerSaveDeepDiveTests(ITestOutputHelper output)
    {
        _output = output;
        AbioticSaveClasses.EnsureLoaded();
    }

    // ---------- location of the server save tree ----------

    /// <summary>
    /// The dedicated-server save root (<c>fixtures/DedicatedServerSaves</c>, containing <c>Worlds/</c>
    /// and <c>Admin.ini</c>): derived from
    /// <see cref="Fixtures.ServerWorldsDir"/>, which points two levels down at
    /// <c>Worlds/Cascade</c>. Null when absent so every test can skip gracefully.
    /// </summary>
    internal static string? ServerRoot { get; } =
        Fixtures.ServerWorldsDir is { } cascade
            ? Path.GetFullPath(Path.Combine(cascade, "..", ".."))
            : null;

    private bool SkipIfMissing()
    {
        if (ServerRoot is null)
        {
            _output.WriteLine("SKIP: no Server fixture tree found.");
            return true;
        }
        return false;
    }

    /// <summary>All .sav files in the live <c>Worlds/</c> tree (no backups).</summary>
    private static IEnumerable<string> LiveSavFiles()
    {
        if (ServerRoot is null) yield break;
        foreach (var path in Directory.EnumerateFiles(Path.Combine(ServerRoot, "Worlds"), "*.sav", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            yield return path;
        }
    }

    /// <summary>All .sav files of one backup generation, empty when absent.</summary>
    private static IEnumerable<string> BackupSavFiles(int generation)
    {
        if (ServerRoot is null) yield break;
        var root = Path.Combine(ServerRoot, "Backups");
        if (!Directory.Exists(root)) yield break;
        foreach (var world in Directory.EnumerateDirectories(root))
        {
            var gen = Path.Combine(world, generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!Directory.Exists(gen)) continue;
            foreach (var path in Directory.EnumerateFiles(gen, "*.sav", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    // =====================================================================
    // 1. Tree report + layout comparison vs client tree and fixture
    // =====================================================================

    [Fact]
    public void Report_ServerTree()
    {
        if (SkipIfMissing()) return;

        _output.WriteLine($"Server root: {ServerRoot}");
        _output.WriteLine("--- live Worlds/ ---");
        foreach (var path in Directory.EnumerateFiles(Path.Combine(ServerRoot!, "Worlds"), "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(ServerRoot!, path);
            var size = new FileInfo(path).Length;
            if (!path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"{size,12:N0}  {rel}  (non-sav)");
                continue;
            }
            string cls;
            try { cls = SaveFolderScanner.ReadSaveClassFromHeader(path) ?? "<null>"; }
            catch (Exception ex) { cls = $"<header error: {ex.Message}>"; }
            _output.WriteLine($"{size,12:N0}  {rel}  [{ShortClass(cls)}] abf={ReadAbfHeader(path)}");
        }

        // Engine version of one representative file per tree.
        var liveWorld = LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p) == "WorldSave_MetaData.sav");
        if (liveWorld is not null)
            _output.WriteLine($"server engine: {ReadEngineVersion(liveWorld)}");
        if (Fixtures.CascadeDir is not null)
            _output.WriteLine($"fixture engine: {ReadEngineVersion(Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav"))}");
        var clientMeta = NewSaveDeepDiveTests.NewSavesRoot is { } client
            ? Directory.EnumerateFiles(client, "WorldSave_MetaData.sav", SearchOption.AllDirectories)
                .FirstOrDefault(p => !p.Contains("Backups", StringComparison.OrdinalIgnoreCase))
            : null;
        if (clientMeta is not null)
            _output.WriteLine($"client engine: {ReadEngineVersion(clientMeta)}");

        // Backup generations: file count, byte total, and any name drift vs live.
        _output.WriteLine("--- Backups/ generations ---");
        var liveNames = LiveSavFiles().Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var gen = 1; gen <= 9; gen++)
        {
            var files = BackupSavFiles(gen).ToList();
            if (files.Count == 0) continue;
            var bytes = files.Sum(f => new FileInfo(f).Length);
            var genNames = files.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = liveNames.Except(genNames).ToList();
            var extra = genNames.Except(liveNames).ToList();
            _output.WriteLine($"  gen {gen}: {files.Count} .sav, {bytes:N0} B" +
                              (missing.Count > 0 ? $", missing vs live: {string.Join(", ", missing)}" : "") +
                              (extra.Count > 0 ? $", extra vs live: {string.Join(", ", extra)}" : ""));
        }

        // Non-sav config files.
        _output.WriteLine("--- ini files ---");
        foreach (var ini in Directory.EnumerateFiles(ServerRoot!, "*.ini", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            _output.WriteLine($"  {Path.GetRelativePath(ServerRoot!, ini)} ({new FileInfo(ini).Length} B)");
        }

        // Region coverage diff vs fixture and client.
        var serverWorlds = LiveSavFiles().Select(Path.GetFileName).Where(n => n!.StartsWith("WorldSave_", StringComparison.Ordinal))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Fixtures.CascadeDir is not null)
        {
            var fixtureWorlds = Directory.EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav")
                .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _output.WriteLine($"--- regions in server but NOT fixture: {string.Join(", ", serverWorlds.Except(fixtureWorlds).OrderBy(x => x).DefaultIfEmpty("<none>"))}");
            _output.WriteLine($"--- regions in fixture but NOT server: {string.Join(", ", fixtureWorlds.Except(serverWorlds).OrderBy(x => x).DefaultIfEmpty("<none>"))}");
        }
        if (NewSaveDeepDiveTests.NewSavesRoot is { } clientRoot)
        {
            var clientCascade = Path.Combine(clientRoot, "76561197993781479", "Worlds", "Cascade");
            if (Directory.Exists(clientCascade))
            {
                var clientWorlds = Directory.EnumerateFiles(clientCascade, "WorldSave_*.sav")
                    .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _output.WriteLine($"--- regions in server but NOT client Cascade: {string.Join(", ", serverWorlds.Except(clientWorlds).OrderBy(x => x).DefaultIfEmpty("<none>"))}");
                _output.WriteLine($"--- regions in client Cascade but NOT server: {string.Join(", ", clientWorlds.Except(serverWorlds).OrderBy(x => x).DefaultIfEmpty("<none>"))}");
            }
        }
    }

    private static string ShortClass(string cls)
        => cls.Contains('.') ? cls[(cls.LastIndexOf('.') + 1)..] : cls;

    // =====================================================================
    // 2. Typed-reader parse checks (live Worlds + one backup generation)
    // =====================================================================

    [Fact]
    public void Parse_ServerPlayerSaves()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        var files = LiveSavFiles().Concat(BackupSavFiles(1))
            .Where(p => Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal));
        foreach (var path in files)
        {
            var rel = Path.GetRelativePath(ServerRoot!, path);
            try
            {
                var data = PlayerSaveReader.ReadFromFile(path);
                _output.WriteLine(
                    $"OK {rel}: skills={data.Skills.Count} traits={data.Traits.Count} phd={data.Phd} " +
                    $"equip={data.Inventory.Equipment.Count} hotbar={data.Inventory.Hotbar.Count} main={data.Inventory.Main.Count} " +
                    $"recipes={data.Recipes.Count} respawn=({data.RespawnX:F0},{data.RespawnY:F0},{data.RespawnZ:F0}) terminal={data.TerminalRespawnId}");
            }
            catch (Exception ex)
            {
                failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (var f in failures) _output.WriteLine($"FAIL {f}");
        Assert.Empty(failures);
    }

    [Fact]
    public void Parse_ServerWorldSaves()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        var files = LiveSavFiles().Concat(BackupSavFiles(1))
            .Where(p => Path.GetFileName(p).StartsWith("WorldSave_", StringComparison.Ordinal));
        foreach (var path in files)
        {
            var rel = Path.GetRelativePath(ServerRoot!, path);
            try
            {
                var world = WorldSaveReader.ReadFromFile(path);
                _output.WriteLine(
                    $"OK {rel}: containers={world.Containers.Count} flags={world.Flags.Count} doors={world.Doors.Count} " +
                    $"dropped={world.DroppedItems.Count} npcs={world.Npcs.Count} deployables={world.Deployables.Count}" +
                    (world.StoryProgressionRow is null ? "" : $" story={world.StoryProgressionRow} minutes={world.MinutesPassed}"));
            }
            catch (Exception ex)
            {
                failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (var f in failures) _output.WriteLine($"FAIL {f}");
        Assert.Empty(failures);
    }

    // =====================================================================
    // 3. Binary round-trip - the corruption canary (every server file)
    // =====================================================================

    [Fact]
    public void RoundTrip_ServerSaves_ByteIdentical()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        var count = 0;
        var allFiles = LiveSavFiles()
            .Concat(Enumerable.Range(1, 9).SelectMany(BackupSavFiles));
        foreach (var path in allFiles)
        {
            var rel = Path.GetRelativePath(ServerRoot!, path);
            var original = File.ReadAllBytes(path);
            count++;
            try
            {
                SaveGame save;
                using (var input = new MemoryStream(original))
                {
                    save = SaveGame.LoadFrom(input);
                }

                byte[] rewritten;
                using (var outputStream = new MemoryStream())
                {
                    save.WriteTo(outputStream);
                    rewritten = outputStream.ToArray();
                }

                if (!original.AsSpan().SequenceEqual(rewritten))
                {
                    var offset = FirstDifference(original, rewritten);
                    failures.Add(rel);
                    _output.WriteLine($"DIFF {rel}: original={original.Length:N0} B rewritten={rewritten.Length:N0} B first diff at 0x{offset:X}");
                    _output.WriteLine($"     strings before diff: {NearbyAscii(original, offset)}");
                    _output.WriteLine($"     orig: {HexWindow(original, offset)}");
                    _output.WriteLine($"     new : {HexWindow(rewritten, offset)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add(rel);
                _output.WriteLine($"LOAD FAIL {rel}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine(failures.Count == 0
            ? $"All {count} server saves (live + every backup generation) round-trip byte-identical."
            : $"{failures.Count}/{count} file(s) did NOT round-trip: {string.Join(", ", failures)}");
        Assert.Empty(failures);
    }

    /// <summary>
    /// JSON export + import (the <see cref="SaveJsonBridge"/> path) for a representative
    /// sample: MetaData, one small world, one large late-game world, one player save.
    /// Asserts byte-identity AND that the ABF_SAVE_VERSION header survives - the header
    /// used to be zeroed before JSON save-class serializers were registered.
    /// </summary>
    [Fact]
    public void JsonRoundTrip_ServerSamples()
    {
        if (SkipIfMissing()) return;

        string?[] names =
        {
            "WorldSave_MetaData.sav",          // metadata save class
            "WorldSave_H_Japan.sav",           // small world save
            "WorldSave_Facility.sav",          // largest world save (16+ MB)
            "WorldSave_Facility_DF_Labs.sav",  // late-game region
        };
        var picks = names
            .Select(n => LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p).Equals(n, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        var player = LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal));
        if (player is not null) picks.Add(player);

        var failures = new List<string>();
        foreach (var path in picks)
        {
            var rel = Path.GetRelativePath(ServerRoot!, path);
            var original = File.ReadAllBytes(path);
            try
            {
                var serializer = new SaveGameSerializer();
                using var json = new MemoryStream();
                using (var input = new MemoryStream(original))
                {
                    serializer.ConvertToJson(input, json);
                }
                json.Position = 0;
                using var rebuilt = new MemoryStream();
                serializer.ConvertFromJson(json, rebuilt);
                var bytes = rebuilt.ToArray();

                var origAbf = ScanAbfVersion(original);
                var newAbf = ScanAbfVersion(bytes);
                if (original.AsSpan().SequenceEqual(bytes))
                {
                    _output.WriteLine($"OK   {rel} (json={json.Length:N0} B, abf={origAbf})");
                }
                else
                {
                    var offset = FirstDifference(original, bytes);
                    failures.Add(rel);
                    _output.WriteLine($"DIFF {rel}: first diff at 0x{offset:X} ({original.Length:N0} → {bytes.Length:N0} B) abf {origAbf} → {newAbf}");
                    _output.WriteLine($"     strings before diff: {NearbyAscii(original, offset)}");
                    _output.WriteLine($"     orig: {HexWindow(original, offset)}");
                    _output.WriteLine($"     new : {HexWindow(bytes, offset)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add(rel);
                _output.WriteLine($"JSON FAIL {rel}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.Empty(failures);
    }

    // =====================================================================
    // 4. Schema diff - late-game regions vs the fixture
    // =====================================================================

    /// <summary>Top-level world-save keys the reader consumes (same list as NewSaveDeepDiveTests).</summary>
    private static readonly string[] ConsumedWorldKeys =
    {
        "DeployedObjectMap", "CustomInventoryMap", "WorldFlags",
        "SimpleDoorMap", "SecurityDoorMap",
        "StoryProgressionRow", "MinutesPassed", "GlobalUnlocks",
        "DroppedItemMap", "NarrativeNPCMap",
    };

    /// <summary>Keys documented in docs/world-save-schema.md + docs/research-new-save-gaps.md as known-but-unmodeled.</summary>
    private static readonly string[] KnownUnmodeledWorldKeys =
    {
        "ResourceNodeMap", "ElevatorMap", "ButtonMap", "PowerSocketMap", "TimeOfDay",
        "TriggerMap", "NPCSpawnMap", "PortalMap", "LevelGUID", "VehicleMap", "TramMap",
        "PetNPC", "SaveIdentifier", "SaveVersion", "DestructibleMap",
        // Metadata-save keys (Abiotic_WorldMetadataSave_C):
        "LeyakContainmentIDs", "ServerEntitlements",
    };

    /// <summary>Regions present on the server but absent from the checked-in fixture.</summary>
    private static readonly string[] LateGameRegions =
    {
        "WorldSave_Facility_Botanical.sav", "WorldSave_Facility_DarkFusion.sav",
        "WorldSave_Facility_DF_Central.sav", "WorldSave_Facility_DF_Labs.sav",
        "WorldSave_Facility_DF_Overgrowth.sav", "WorldSave_Facility_DF_War.sav",
        "WorldSave_Facility_Fracture.sav", "WorldSave_Facility_Plant.sav",
        "WorldSave_Facility_Pool.sav", "WorldSave_Facility_Residence.sav",
        "WorldSave_V_Anteverse_C.sav", "WorldSave_V_BOTANICAL.sav",
        "WorldSave_V_Inq.sav", "WorldSave_V_ISLAND.sav", "WorldSave_V_Signal.sav",
        "WorldSave_V_SUOMI.sav", "WorldSave_V_TheWall.sav",
    };

    [Fact]
    public void SchemaDiff_LateGameRegions()
    {
        if (SkipIfMissing()) return;

        // Union of top-level keys across ALL fixture world saves - "version-new" means
        // absent from every fixture file, not merely missing from the modeled-key lists.
        var fixtureKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Fixtures.CascadeDir is not null)
        {
            foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav"))
            {
                using var ffs = File.OpenRead(path);
                foreach (var tag in SaveGame.LoadFrom(ffs).Properties!)
                {
                    fixtureKeys.Add(StripHash(tag.Name.Value));
                }
            }
        }

        // --- 4a. top-level key census over the late-game regions ---
        var unmodeledKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var versionNewKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in LateGameRegions)
        {
            var path = LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (path is null)
            {
                _output.WriteLine($"{name}: <absent>");
                continue;
            }

            using var fs = File.OpenRead(path);
            var save = SaveGame.LoadFrom(fs);
            var unknown = new List<string>();
            foreach (var tag in save.Properties!)
            {
                var stripped = StripHash(tag.Name.Value);
                if (fixtureKeys.Count > 0 && !fixtureKeys.Contains(stripped)) versionNewKeys.Add(stripped);
                if (!ConsumedWorldKeys.Contains(stripped, StringComparer.OrdinalIgnoreCase)
                    && !KnownUnmodeledWorldKeys.Contains(stripped, StringComparer.OrdinalIgnoreCase))
                {
                    unknown.Add($"{stripped} ({tag.Type})");
                    unmodeledKeys.Add(stripped);
                }
            }
            _output.WriteLine($"{name}: {save.Properties!.Count} top-level keys" +
                              (unknown.Count > 0 ? $", unmodeled: {string.Join(", ", unknown)}" : ""));
        }
        _output.WriteLine($"--- unmodeled top-level keys across late-game regions: {string.Join(", ", unmodeledKeys.DefaultIfEmpty("<none>"))} ---");
        _output.WriteLine($"--- keys absent from EVERY fixture file (version-new): {string.Join(", ", versionNewKeys.DefaultIfEmpty("<none>"))} ---");

        // --- 4b. deployable class names new to the server tree ---
        var fixtureClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Fixtures.CascadeDir is not null)
        {
            foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav"))
            {
                foreach (var d in WorldSaveReader.ReadFromFile(path).Deployables)
                {
                    if (d.ClassName is not null) fixtureClasses.Add(d.ClassName);
                }
            }
        }
        var fixtureNpcActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Fixtures.CascadeDir is not null)
        {
            foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav"))
            {
                foreach (var npc in WorldSaveReader.ReadFromFile(path).Npcs)
                {
                    fixtureNpcActors.Add(NpcActorClass(npc.Id));
                }
            }
        }

        var serverClasses = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var serverNpcActors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var serverFlags = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in LiveSavFiles().Where(p => Path.GetFileName(p).StartsWith("WorldSave_", StringComparison.Ordinal)))
        {
            var world = WorldSaveReader.ReadFromFile(path);
            foreach (var d in world.Deployables)
            {
                if (d.ClassName is not null) serverClasses.Add(d.ClassName);
            }
            foreach (var f in world.Flags) serverFlags.Add(f);
            foreach (var npc in world.Npcs) serverNpcActors.Add(NpcActorClass(npc.Id));
        }

        var newClasses = serverClasses.Where(c => !fixtureClasses.Contains(c)).ToList();
        _output.WriteLine($"--- deployable classes: server={serverClasses.Count}, fixture={fixtureClasses.Count}, new on server ({newClasses.Count}): ---");
        foreach (var c in newClasses) _output.WriteLine($"  {c}");

        var newNpcActors = serverNpcActors.Where(c => !fixtureNpcActors.Contains(c)).ToList();
        _output.WriteLine($"--- NarrativeNPC actor classes: server={serverNpcActors.Count}, fixture={fixtureNpcActors.Count}, new on server ({newNpcActors.Count}): ---");
        foreach (var c in newNpcActors) _output.WriteLine($"  {c}");

        // --- 4c. world flags new to the server tree (sample) ---
        var fixtureFlags = new HashSet<string>(StringComparer.Ordinal);
        if (Fixtures.CascadeDir is not null)
        {
            foreach (var path in Directory.EnumerateFiles(Fixtures.CascadeDir, "WorldSave_*.sav"))
            {
                foreach (var f in WorldSaveReader.ReadFromFile(path).Flags) fixtureFlags.Add(f);
            }
        }
        var newFlags = serverFlags.Where(f => !fixtureFlags.Contains(f)).ToList();
        _output.WriteLine($"--- world flags: server={serverFlags.Count}, fixture={fixtureFlags.Count}, new on server ({newFlags.Count}), sample: ---");
        foreach (var f in newFlags.Take(60)) _output.WriteLine($"  {f}");

        // --- 4d. ABF_SAVE_VERSION comparison across the three trees ---
        _output.WriteLine("--- ABF_SAVE_VERSION (Version/Id) per tree ---");
        void DumpAbf(string label, string? path)
        {
            if (path is null) { _output.WriteLine($"  {label}: <absent>"); return; }
            _output.WriteLine($"  {label}: {ReadAbfHeader(path)}  ({Path.GetFileName(path)})");
        }
        DumpAbf("server world  ", LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p) == "WorldSave_Facility.sav"));
        DumpAbf("server meta   ", LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p) == "WorldSave_MetaData.sav"));
        DumpAbf("server player ", LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal)));
        if (Fixtures.CascadeDir is not null)
        {
            DumpAbf("fixture world ", Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav"));
            DumpAbf("fixture meta  ", Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav"));
            DumpAbf("fixture player", Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav").FirstOrDefault());
        }
        if (NewSaveDeepDiveTests.NewSavesRoot is { } client)
        {
            var clientFiles = Directory.EnumerateFiles(client, "*.sav", SearchOption.AllDirectories)
                .Where(p => !p.Contains("Backups", StringComparison.OrdinalIgnoreCase)).ToList();
            DumpAbf("client world  ", clientFiles.FirstOrDefault(p => Path.GetFileName(p) == "WorldSave_Facility.sav"));
            DumpAbf("client meta   ", clientFiles.FirstOrDefault(p => Path.GetFileName(p) == "WorldSave_MetaData.sav"));
            DumpAbf("client player ", clientFiles.FirstOrDefault(p => Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal)));
        }

        // --- 4e. value-struct field census for the big late-game maps ---
        var dfLabs = LiveSavFiles().FirstOrDefault(p => Path.GetFileName(p) == "WorldSave_Facility_DF_Labs.sav");
        if (dfLabs is not null)
        {
            using var fs = File.OpenRead(dfLabs);
            var save = SaveGame.LoadFrom(fs);
            DumpMapValueFieldCensus(save, "DeployedObjectMap");
            DumpMapValueFieldCensus(save, "NarrativeNPCMap");
            DumpMapValueFieldCensus(save, "DestructibleMap");
            DumpMapValueFieldCensus(save, "NPCSpawnMap");
            DumpMapValueFieldCensus(save, "CorpseMap");

            // CorpseMap sample entries (key + value summary).
            foreach (var kvp in (GetMapPairs(save.Properties, "CorpseMap") ?? []).Take(4))
            {
                _output.WriteLine($"  CorpseMap sample: {kvp.Key.Value} => {(kvp.Value is StructProperty { Value: PropertiesStruct cps } ? string.Join(", ", cps.Properties.Select(p => $"{StripHash(p.Name.Value)}={p.Property?.Value}")) : kvp.Value.Value?.ToString())}");
            }
        }
    }

    /// <summary>
    /// Actor class from a NarrativeNPCMap key:
    /// <c>...PersistentLevel.NarrativeNPC_Human_Hologram_C_2</c> -> <c>NarrativeNPC_Human_Hologram_C</c>.
    /// </summary>
    private static string NpcActorClass(string actorPath)
    {
        var idx = actorPath.LastIndexOf('.');
        var name = idx >= 0 ? actorPath[(idx + 1)..] : actorPath;
        return Regex.Replace(name, "_\\d+$", "");
    }

    private void DumpMapValueFieldCensus(SaveGame save, string mapName)
    {
        var pairs = GetMapPairs(save.Properties, mapName);
        if (pairs is null)
        {
            _output.WriteLine($"{mapName}: <absent>");
            return;
        }

        var fields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var nonStructValues = 0;
        foreach (var kvp in pairs)
        {
            if (kvp.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
            {
                foreach (var f in ps.Properties)
                {
                    var key = $"{StripHash(f.Name.Value)} ({f.Type})";
                    fields[key] = fields.GetValueOrDefault(key) + 1;
                }
            }
            else
            {
                nonStructValues++;
            }
        }

        _output.WriteLine($"--- {mapName}: {pairs.Count} entries, value fields ---");
        foreach (var (field, count) in fields.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            _output.WriteLine($"  {count,5} × {field}");
        if (nonStructValues > 0)
            _output.WriteLine($"  ({nonStructValues} non-struct values)");
    }

    // =====================================================================
    // 5. SandboxSettings.ini / Admin.ini
    // =====================================================================

    [Fact]
    public void Report_ServerIniFiles()
    {
        if (SkipIfMissing()) return;

        var admin = Path.Combine(ServerRoot!, "Admin.ini");
        if (File.Exists(admin))
        {
            _output.WriteLine($"--- Admin.ini ---");
            _output.WriteLine(File.ReadAllText(admin));
        }

        var sandbox = Directory.EnumerateFiles(Path.Combine(ServerRoot!, "Worlds"), "SandboxSettings.ini", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (sandbox is null)
        {
            _output.WriteLine("SandboxSettings.ini: <absent>");
            return;
        }

        _output.WriteLine($"--- {Path.GetRelativePath(ServerRoot!, sandbox)} (raw) ---");
        var lines = File.ReadAllLines(sandbox);
        foreach (var line in lines) _output.WriteLine(line);

        // Effective values: ini semantics are last-key-wins; the game appends rather
        // than rewrites, so duplicates are expected.
        var effective = new Dictionary<string, (string Value, int Occurrences)>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.StartsWith(';')) continue;
            var eq = trimmed.IndexOf('=');
            if (eq <= 0) continue;
            var key = trimmed[..eq].Trim();
            var value = trimmed[(eq + 1)..].Trim();
            effective[key] = (value, effective.TryGetValue(key, out var prev) ? prev.Occurrences + 1 : 1);
        }

        _output.WriteLine("--- effective (last occurrence wins) ---");
        foreach (var (key, (value, occurrences)) in effective.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            _output.WriteLine($"  {key} = {value}" + (occurrences > 1 ? $"  ({occurrences} occurrences)" : ""));

        Assert.NotEmpty(effective);
    }

    // =====================================================================
    // shared helpers (mirrors NewSaveDeepDiveTests; that class keeps them private)
    // =====================================================================

    /// <summary>Reads engine version + custom-format count straight out of the GVAS header.</summary>
    private static string ReadEngineVersion(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var reader = new BinaryReader(fs, Encoding.ASCII);
            if (reader.ReadUInt32() != 0x53415647) return "<not GVAS>";
            var saveGameVersion = reader.ReadInt32();
            var ue4 = reader.ReadUInt32();
            uint ue5 = 0;
            if (saveGameVersion >= 3) ue5 = reader.ReadUInt32();
            var major = reader.ReadInt16();
            var minor = reader.ReadInt16();
            var patch = reader.ReadInt16();
            var build = reader.ReadInt32();
            var len = reader.ReadInt32();
            var branch = len > 0 ? Encoding.ASCII.GetString(reader.ReadBytes(len), 0, len - 1) : "";
            reader.ReadInt32();
            var formats = reader.ReadUInt32();
            return $"gvas v{saveGameVersion} ue4pkg={ue4} ue5pkg={ue5} engine={major}.{minor}.{patch}-{build} '{branch}' formats={formats}";
        }
        catch (Exception ex)
        {
            return $"<header error: {ex.Message}>";
        }
    }

    /// <summary>ABF custom header summary for a file, or a marker when absent (e.g. ini).</summary>
    private static string ReadAbfHeader(string path)
    {
        try
        {
            var head = new byte[Math.Min(4096, new FileInfo(path).Length)];
            using (var fs = File.OpenRead(path))
            {
                fs.ReadExactly(head);
            }
            return ScanAbfVersion(head);
        }
        catch (Exception ex)
        {
            return $"<error: {ex.Message}>";
        }
    }

    /// <summary>Finds "ABF_SAVE_VERSION" and reads the Version/Id ints after its terminator.</summary>
    private static string ScanAbfVersion(byte[] bytes)
    {
        var needle = Encoding.ASCII.GetBytes("ABF_SAVE_VERSION");
        for (var i = 0; i <= bytes.Length - needle.Length - 9; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (bytes[i + j] != needle[j]) { match = false; break; }
            }
            if (!match) continue;

            var after = i + needle.Length + 1; // skip NUL terminator
            var version = BitConverter.ToInt32(bytes, after);
            var id = BitConverter.ToInt32(bytes, after + 4);
            return $"v{version}/id{id}";
        }
        return "<no ABF header>";
    }

    private static long FirstDifference(byte[] a, byte[] b)
    {
        var min = Math.Min(a.Length, b.Length);
        for (var i = 0; i < min; i++)
        {
            if (a[i] != b[i]) return i;
        }
        return min;
    }

    /// <summary>Readable ASCII runs (≥5 chars) in the 512 bytes before <paramref name="offset"/>.</summary>
    private static string NearbyAscii(byte[] data, long offset, int window = 512)
    {
        var start = (int)Math.Max(0, offset - window);
        var end = (int)Math.Min(data.Length, offset + 64);
        var runs = new List<string>();
        var sb = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            var c = (char)data[i];
            if (c >= 0x20 && c < 0x7F)
            {
                sb.Append(c);
            }
            else
            {
                if (sb.Length >= 5) runs.Add(sb.ToString());
                sb.Clear();
            }
        }
        if (sb.Length >= 5) runs.Add(sb.ToString());
        return string.Join(" | ", runs.TakeLast(8));
    }

    private static string HexWindow(byte[] data, long offset, int radius = 24)
    {
        var start = (int)Math.Max(0, offset - radius);
        var end = (int)Math.Min(data.Length, offset + radius);
        var sb = new StringBuilder();
        for (var i = start; i < end; i++)
        {
            if (i == offset) sb.Append('[');
            sb.Append(data[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            if (i == offset) sb.Append(']');
            sb.Append(' ');
        }
        return sb.ToString();
    }

    /// <summary>Strips the blueprint hash suffix: <c>Hunger_2_A6C5...CA</c> -> <c>Hunger</c>.</summary>
    private static string StripHash(string name)
        => Regex.Replace(name, "_\\d+_[0-9A-F]{32}$", "");

    private static FPropertyTag? FindByPrefix(IEnumerable<FPropertyTag> tags, string prefix)
        => tags.FirstOrDefault(t => t.Name?.Value is { } n && n.StartsWith(prefix, StringComparison.Ordinal));

    private static IList<KeyValuePair<FProperty, FProperty>>? GetMapPairs(IList<FPropertyTag>? topLevel, string namePrefix)
        => topLevel is not null && FindByPrefix(topLevel, namePrefix)?.Property is MapProperty mp ? mp.Value : null;
}
