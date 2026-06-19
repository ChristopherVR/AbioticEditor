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
/// Deep dive over the client save fixture tree (<c>fixtures/SteamSaves/SaveGames</c>,
/// a copy of the game's own <c>Saved/SaveGames</c> - a NEWER game version than
/// the checked-in <c>SteamSaves/Legacy/Cascade/</c> fixture). Goal: find what the editor's models miss or
/// get wrong on current-version saves. Findings are written to
/// <c>docs/research-new-save-gaps.md</c>.
///
/// All tests guard on the live tree being present and skip gracefully when it is not
/// (e.g. on CI), mirroring how <see cref="Fixtures.CascadeDir"/> is treated.
/// </summary>
public class NewSaveDeepDiveTests
{
    private readonly ITestOutputHelper _output;

    public NewSaveDeepDiveTests(ITestOutputHelper output)
    {
        _output = output;
        AbioticSaveClasses.EnsureLoaded();
    }

    // ---------- location of the live save tree ----------

    /// <summary>
    /// <c>tests/fixtures/SteamSaves/SaveGames</c> - a copy of the game's own
    /// Saved/SaveGames folder, located via <see cref="Fixtures.ClientSavedDir"/>.
    /// Null when absent so every test can skip gracefully.
    /// </summary>
    internal static string? NewSavesRoot { get; } = Fixtures.ClientSavedDir;

    /// <summary>All .sav files in the live tree, optionally excluding the Backups copies
    /// (which duplicate <c>Worlds/Cascade</c> at the same game version).</summary>
    private static IEnumerable<string> NewSavFiles(bool includeBackups = false)
    {
        if (NewSavesRoot is null) yield break;
        foreach (var path in Directory.EnumerateFiles(NewSavesRoot, "*.sav", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (!includeBackups && path.Contains($"{Path.DirectorySeparatorChar}Backups{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                continue;
            yield return path;
        }
    }

    private bool SkipIfMissing()
    {
        if (NewSavesRoot is null)
        {
            _output.WriteLine("SKIP: no SteamSaves/SaveGames fixture tree found.");
            return true;
        }
        return false;
    }

    // =====================================================================
    // 1. File tree + save classes + engine versions
    // =====================================================================

    [Fact]
    public void Report_SaveTree()
    {
        if (SkipIfMissing()) return;

        _output.WriteLine($"Root: {NewSavesRoot}");
        foreach (var path in Directory.EnumerateFiles(NewSavesRoot!, "*", SearchOption.AllDirectories)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(NewSavesRoot!, path);
            var size = new FileInfo(path).Length;
            if (!path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"{size,12:N0}  {rel}  (non-sav)");
                continue;
            }

            string cls;
            try
            {
                cls = SaveFolderScanner.ReadSaveClassFromHeader(path) ?? "<null>";
            }
            catch (Exception ex)
            {
                cls = $"<header error: {ex.Message}>";
            }
            _output.WriteLine($"{size,12:N0}  {rel}  [{cls}] {ReadEngineVersion(path)}");
        }

        // Engine version of the old fixture for comparison.
        if (Fixtures.CascadeDir is not null)
        {
            var fixturePlayer = Directory.EnumerateFiles(
                Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav").FirstOrDefault();
            if (fixturePlayer is not null)
            {
                _output.WriteLine($"(old fixture engine: {ReadEngineVersion(fixturePlayer)})");
            }
        }
    }

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

    // =====================================================================
    // 2. Typed-reader parse checks
    // =====================================================================

    [Fact]
    public void Parse_NewPlayerSaves()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        foreach (var path in NewSavFiles().Where(p => Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal)))
        {
            var rel = Path.GetRelativePath(NewSavesRoot!, path);
            try
            {
                var data = PlayerSaveReader.ReadFromFile(path);
                _output.WriteLine(
                    $"OK {rel}: skills={data.Skills.Count} traits={data.Traits.Count} phd={data.Phd} " +
                    $"equip={data.Inventory.Equipment.Count} hotbar={data.Inventory.Hotbar.Count} main={data.Inventory.Main.Count} " +
                    $"recipes={data.Recipes.Count} transmog={data.TransmogSlots.Count}/{data.TransmogVisibility.Count} " +
                    $"respawn=({data.RespawnX:F0},{data.RespawnY:F0},{data.RespawnZ:F0}) terminal={data.TerminalRespawnId}");
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
    public void Parse_NewWorldSaves()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        foreach (var path in NewSavFiles().Where(p => Path.GetFileName(p).StartsWith("WorldSave_", StringComparison.Ordinal)))
        {
            var rel = Path.GetRelativePath(NewSavesRoot!, path);
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

    [Fact]
    public void Parse_AccountLevelSaves()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        foreach (var path in NewSavFiles().Where(p => !Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal)
                                                      && !Path.GetFileName(p).StartsWith("WorldSave_", StringComparison.Ordinal)))
        {
            var rel = Path.GetRelativePath(NewSavesRoot!, path);
            var name = Path.GetFileName(path);
            try
            {
                if (name.StartsWith("ScientistCustomization_", StringComparison.OrdinalIgnoreCase))
                {
                    var file = CustomizationSaveFile.LoadFromFile(path);
                    _output.WriteLine($"OK {rel}: {file.Fields.Count}/{CustomizationSaveFile.KnownFields.Count} known fields");
                    foreach (var f in file.Fields)
                        _output.WriteLine($"   {f.PropertyName} = {f.CurrentValue}");

                    // Gap check: any top-level property the customization model does not know?
                    using var fs = File.OpenRead(path);
                    var save = SaveGame.LoadFrom(fs);
                    foreach (var tag in save.Properties!)
                    {
                        var known = CustomizationSaveFile.KnownFields.Any(k =>
                            string.Equals(k.PropertyName, tag.Name.Value, StringComparison.OrdinalIgnoreCase));
                        if (!known)
                            _output.WriteLine($"   UNKNOWN field: {tag.Name.Value} ({tag.Type}) = {tag.Property?.Value}");
                    }
                }
                else
                {
                    using var fs = File.OpenRead(path);
                    var save = SaveGame.LoadFrom(fs);
                    _output.WriteLine($"OK {rel}: class={save.SaveClass} props={save.Properties?.Count}");
                    foreach (var tag in save.Properties ?? Enumerable.Empty<FPropertyTag>())
                        _output.WriteLine($"   {tag.Name.Value} ({tag.Type}) = {Summarize(tag.Property)}");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (var f in failures) _output.WriteLine($"FAIL {f}");
        Assert.Empty(failures);
    }

    private static string Summarize(FProperty? prop)
    {
        return prop switch
        {
            null => "<null>",
            ArrayProperty { Value.Length: > 0 and <= 12 } a =>
                $"Array[{a.Value.Length}]{{{string.Join(", ", Enumerable.Range(0, a.Value.Length).Select(i => a.Value.GetValue(i)))}}}",
            ArrayProperty a => $"Array[{a.Value?.Length ?? 0}]",
            MapProperty { Value.Count: > 0 and <= 12 } m =>
                $"Map[{m.Value.Count}]{{{string.Join(", ", m.Value.Select(kv => $"{kv.Key.Value}={kv.Value.Value}"))}}}",
            MapProperty m => $"Map[{m.Value?.Count ?? 0}]",
            StructProperty { Value: PropertiesStruct ps } => $"Struct{{{string.Join(", ", ps.Properties.Select(p => StripHash(p.Name.Value)))}}}",
            StructProperty s => $"Struct<{s.Value?.GetType().Name}> {s.Value}",
            _ => prop.Value?.ToString() ?? "<null>",
        };
    }

    // =====================================================================
    // 3. Binary round-trip - the corruption canary
    // =====================================================================

    [Fact]
    public void RoundTrip_NewSaves_ByteIdentical()
    {
        if (SkipIfMissing()) return;

        var failures = new List<string>();
        foreach (var path in NewSavFiles())
        {
            var rel = Path.GetRelativePath(NewSavesRoot!, path);
            var original = File.ReadAllBytes(path);
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

                if (original.AsSpan().SequenceEqual(rewritten))
                {
                    _output.WriteLine($"OK   {rel} ({original.Length:N0} B)");
                }
                else
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

        _output.WriteLine("");
        _output.WriteLine(failures.Count == 0
            ? "All new saves round-trip byte-identical."
            : $"{failures.Count} file(s) did NOT round-trip: {string.Join(", ", failures)}");
        Assert.Empty(failures);
    }

    /// <summary>
    /// JSON bridge canary: the raw-JSON tab converts sav -> JSON -> sav. If that path is
    /// not byte-identical, editing via JSON corrupts the file.
    ///
    /// KNOWN DEFECT (documented in docs/research-new-save-gaps.md): no JSON
    /// <c>ISaveClassSerializer</c> is registered for the Abiotic custom save classes, so
    /// the <c>ABF_SAVE_VERSION</c> header (Version=3, Id=1) is dropped on export and
    /// zeroed on import - for old AND new saves alike. Byte differences are therefore
    /// reported (not asserted) here; only unexpected exceptions fail the test.
    /// </summary>
    [Fact]
    public void JsonRoundTrip_NewSaves_ReportHeaderLoss()
    {
        if (SkipIfMissing()) return;

        // Representative picks: a player save, the metadata save, and a small + medium
        // world save from the newest world (Chrissie); plus an old-fixture control pair
        // to tell "new-version regression" apart from "always been broken".
        var picks = NewSavFiles()
            .Where(p => p.Contains($"{Path.DirectorySeparatorChar}Chrissie{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p =>
                Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal)
                || Path.GetFileName(p) is "WorldSave_MetaData.sav" or "WorldSave_Facility_MFFoundry.sav" or "WorldSave_Facility_Labs.sav")
            .Concat(NewSavFiles().Where(p => Path.GetFileName(p).StartsWith("ScientistCustomization", StringComparison.OrdinalIgnoreCase)))
            .Select(p => (Path: p, Label: "new"))
            .ToList();
        if (Fixtures.CascadeDir is not null)
        {
            picks.Add((Path.Combine(Fixtures.CascadeDir, "WorldSave_MetaData.sav"), "old-fixture control"));
            var oldPlayer = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav").FirstOrDefault();
            if (oldPlayer is not null) picks.Add((oldPlayer, "old-fixture control"));
        }

        var exceptions = new List<string>();
        var diffs = 0;
        foreach (var (path, label) in picks)
        {
            var rel = Path.GetFileName(Path.GetDirectoryName(path)) + "/" + Path.GetFileName(path);
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

                if (original.AsSpan().SequenceEqual(rebuilt.ToArray()))
                {
                    _output.WriteLine($"OK   [{label}] {rel} (json={json.Length:N0} B)");
                }
                else
                {
                    var bytes = rebuilt.ToArray();
                    var offset = FirstDifference(original, bytes);
                    diffs++;
                    _output.WriteLine($"DIFF [{label}] {rel}: first diff at 0x{offset:X} ({original.Length:N0} → {bytes.Length:N0} B)");
                    _output.WriteLine($"     strings before diff: {NearbyAscii(original, offset)}");
                    _output.WriteLine($"     orig: {HexWindow(original, offset)}");
                    _output.WriteLine($"     new : {HexWindow(bytes, offset)}");
                }
            }
            catch (Exception ex)
            {
                exceptions.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
                _output.WriteLine($"JSON FAIL [{label}] {rel}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _output.WriteLine("");
        _output.WriteLine($"{diffs} file(s) lost the ABF_SAVE_VERSION header through the JSON path.");
        Assert.Empty(exceptions);
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

    /// <summary>Readable ASCII runs (≥5 chars) in the 512 bytes before <paramref name="offset"/>,
    /// usually property/struct names that identify the mis-parsed region.</summary>
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

    // =====================================================================
    // 4. Schema gap analysis - player save
    // =====================================================================

    /// <summary>Prefixes <see cref="PlayerSaveReader"/> consumes from CharacterSaveData.</summary>
    private static readonly string[] ConsumedPlayerPrefixes =
    {
        "CurrentSurvivalStats_", "CurrentMoney_",
        "EquipmentInventory_", "HotbarInventory_", "Inventory_",
        "Skills_", "Traits_", "PhD_", "CharacterHealth_",
        "RecipesUnlock_", "EmailsRead_", "JournalEntries_",
        "Compendium_EmailSections_", "Compendium_NarrativeSections_", "Compendium_ExplorationSections_",
        "ItemsPickedUp_", "CraftedItems_", "MapsUnlocked_",
        "Compendium_KillCount_", "Compendium_Fish_",
        "TransmogInventory_", "TransmogVisibility_",
        "LastSafeWorldLocation_", "LastSafeWorldGUID_", "TerminalRespawnID_",
    };

    [Fact]
    public void SchemaGaps_PlayerSave()
    {
        if (SkipIfMissing()) return;

        var newPlayer = NewSavFiles().FirstOrDefault(p =>
            p.Contains($"{Path.DirectorySeparatorChar}Chrissie{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal));
        newPlayer ??= NewSavFiles().FirstOrDefault(p => Path.GetFileName(p).StartsWith("Player_", StringComparison.Ordinal));
        if (newPlayer is null)
        {
            _output.WriteLine("SKIP: no new player save found.");
            return;
        }

        _output.WriteLine($"New player save: {Path.GetRelativePath(NewSavesRoot!, newPlayer)}");
        using var fs = File.OpenRead(newPlayer);
        var save = SaveGame.LoadFrom(fs);

        _output.WriteLine("--- top-level properties ---");
        foreach (var tag in save.Properties!)
            _output.WriteLine($"  {tag.Name.Value} ({tag.Type})");

        var root = GetCharacterSaveData(save);
        _output.WriteLine($"--- CharacterSaveData: {root.Count} properties ---");
        var unconsumed = new List<string>();
        foreach (var tag in root)
        {
            var name = tag.Name.Value;
            var consumed = ConsumedPlayerPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal));
            _output.WriteLine($"  [{(consumed ? "model" : " GAP ")}] {name} ({tag.Type}) = {Summarize(tag.Property)}");
            if (!consumed) unconsumed.Add(StripHash(name));
        }
        _output.WriteLine($"--- unconsumed ({unconsumed.Count}): {string.Join(", ", unconsumed)} ---");

        // Key-set diff against the old fixture (same steamid when available).
        if (Fixtures.CascadeDir is not null)
        {
            var steamPart = Path.GetFileName(newPlayer);
            var oldPlayer = Path.Combine(Fixtures.CascadeDir, "PlayerData", steamPart);
            if (!File.Exists(oldPlayer))
                oldPlayer = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir, "PlayerData"), "Player_*.sav").First();

            using var oldFs = File.OpenRead(oldPlayer);
            var oldSave = SaveGame.LoadFrom(oldFs);
            var oldRoot = GetCharacterSaveData(oldSave);

            var newKeys = root.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var oldKeys = oldRoot.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newTop = save.Properties!.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var oldTop = oldSave.Properties!.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);

            _output.WriteLine($"--- diff vs old fixture {Path.GetFileName(oldPlayer)} ---");
            _output.WriteLine($"  top-level added:   {string.Join(", ", newTop.Except(oldTop).DefaultIfEmpty("<none>"))}");
            _output.WriteLine($"  top-level removed: {string.Join(", ", oldTop.Except(newTop).DefaultIfEmpty("<none>"))}");
            _output.WriteLine($"  CharacterSaveData added:   {string.Join(", ", newKeys.Except(oldKeys).DefaultIfEmpty("<none>"))}");
            _output.WriteLine($"  CharacterSaveData removed: {string.Join(", ", oldKeys.Except(newKeys).DefaultIfEmpty("<none>"))}");

            // JSON exports (prove the export path works on new saves; handy for manual diffing).
            var exportDir = Path.Combine(Path.GetTempPath(), "abiotic-newsave-deepdive");
            Directory.CreateDirectory(exportDir);
            var newJson = Path.Combine(exportDir, "player-new.json");
            var oldJson = Path.Combine(exportDir, "player-old.json");
            SaveJsonBridge.ExportJsonToFile(save, newJson);
            SaveJsonBridge.ExportJsonToFile(oldSave, oldJson);
            _output.WriteLine($"  JSON exports: {newJson} ({new FileInfo(newJson).Length:N0} B), {oldJson} ({new FileInfo(oldJson).Length:N0} B)");
        }
    }

    // =====================================================================
    // 5. Schema gap analysis - world save
    // =====================================================================

    /// <summary>Top-level world-save keys the reader consumes.</summary>
    private static readonly string[] ConsumedWorldKeys =
    {
        "DeployedObjectMap", "CustomInventoryMap", "WorldFlags",
        "SimpleDoorMap", "SecurityDoorMap",
        "StoryProgressionRow", "MinutesPassed", "GlobalUnlocks",
        "DroppedItemMap", "NarrativeNPCMap",
    };

    /// <summary>Keys documented in docs/world-save-schema.md as known-but-unmodeled.</summary>
    private static readonly string[] KnownUnmodeledWorldKeys =
    {
        "ResourceNodeMap", "ElevatorMap", "ButtonMap", "PowerSocketMap", "TimeOfDay",
        "TriggerMap", "NPCSpawnMap", "PortalMap", "LevelGUID", "VehicleMap", "TramMap",
        "PetNPC", "SaveIdentifier", "SaveVersion",
        // Metadata-save keys (Abiotic_WorldMetadataSave_C):
        "LeyakContainmentIDs", "ServerEntitlements",
    };

    [Fact]
    public void SchemaGaps_WorldSave()
    {
        if (SkipIfMissing()) return;

        // Largest new world save (the v-current Cascade Facility) carries the most shapes.
        var worldPath = NewSavFiles()
            .Where(p => Path.GetFileName(p).StartsWith("WorldSave_", StringComparison.Ordinal))
            .OrderByDescending(p => new FileInfo(p).Length)
            .First();
        _output.WriteLine($"World save: {Path.GetRelativePath(NewSavesRoot!, worldPath)} ({new FileInfo(worldPath).Length:N0} B)");

        using var fs = File.OpenRead(worldPath);
        var save = SaveGame.LoadFrom(fs);

        _output.WriteLine("--- top-level keys ---");
        var newKeys = new List<string>();
        foreach (var tag in save.Properties!)
        {
            var stripped = StripHash(tag.Name.Value);
            var status = ConsumedWorldKeys.Contains(stripped, StringComparer.OrdinalIgnoreCase) ? "model"
                : KnownUnmodeledWorldKeys.Contains(stripped, StringComparer.OrdinalIgnoreCase) ? "known"
                : " NEW ";
            _output.WriteLine($"  [{status}] {tag.Name.Value} ({tag.Type}) = {Summarize(tag.Property)}");
            if (status == " NEW ") newKeys.Add(stripped);
        }
        _output.WriteLine($"--- NEW top-level keys: {string.Join(", ", newKeys.DefaultIfEmpty("<none>"))} ---");

        // Key-set diffs: old fixture facility vs new, and the newest-build (Chrissie) facility.
        if (Fixtures.CascadeDir is not null)
        {
            var oldFacility = Path.Combine(Fixtures.CascadeDir, "WorldSave_Facility.sav");
            if (File.Exists(oldFacility))
            {
                using var oldFs = File.OpenRead(oldFacility);
                var oldSave = SaveGame.LoadFrom(oldFs);
                var oldKeys = oldSave.Properties!.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var curKeys = save.Properties!.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _output.WriteLine($"--- vs old fixture Facility: added={string.Join(", ", curKeys.Except(oldKeys).DefaultIfEmpty("<none>"))} " +
                                  $"removed={string.Join(", ", oldKeys.Except(curKeys).DefaultIfEmpty("<none>"))} ---");
            }
        }
        var chrissieFacility = NewSavFiles().FirstOrDefault(p =>
            p.Contains($"{Path.DirectorySeparatorChar}Chrissie{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(p) == "WorldSave_Facility.sav");
        if (chrissieFacility is not null)
        {
            using var chFs = File.OpenRead(chrissieFacility);
            var chSave = SaveGame.LoadFrom(chFs);
            var chKeys = chSave.Properties!.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var curKeys = save.Properties!.Select(t => StripHash(t.Name.Value)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _output.WriteLine($"--- Chrissie (newest build) Facility keys: extra={string.Join(", ", chKeys.Except(curKeys).DefaultIfEmpty("<none>"))} " +
                              $"missing={string.Join(", ", curKeys.Except(chKeys).DefaultIfEmpty("<none>"))} ---");
        }

        // Field-shape census of map value structs our reader walks.
        DumpMapValueFieldCensus(save, "DeployedObjectMap");
        DumpMapValueFieldCensus(save, "DestructibleMap");
        DumpMapValueFieldCensus(save, "NarrativeNPCMap");
        DumpMapValueFieldCensus(save, "DroppedItemMap");
        DumpMapValueFieldCensus(save, "SimpleDoorMap");
        DumpMapValueFieldCensus(save, "SecurityDoorMap");
        DumpMapValueFieldCensus(save, "CustomInventoryMap");

        // WorldFlags shape check.
        var flagsTag = FindByPrefix(save.Properties!, "WorldFlags");
        _output.WriteLine(flagsTag?.Property is ArrayProperty fa
            ? $"WorldFlags: Array[{fa.Value?.Length}] of {fa.Value?.GetValue(0)?.GetType().Name}"
            : $"WorldFlags: UNEXPECTED shape {flagsTag?.Property?.GetType().Name}");

        // Inventory slot ChangeableData field census (across deployables + dropped items).
        var slotFields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var deployed = GetMapPairs(save.Properties, "DeployedObjectMap");
        if (deployed is not null)
        {
            foreach (var kvp in deployed)
            {
                if (kvp.Value is not StructProperty sp || sp.Value is not PropertiesStruct ps) continue;
                if (FindByPrefix(ps.Properties, "ContainerInventories_")?.Property is not ArrayProperty inv
                    || inv.Value is null) continue;
                for (var i = 0; i < inv.Value.Length; i++)
                {
                    if (inv.Value.GetValue(i) is not StructProperty isp || isp.Value is not PropertiesStruct ips) continue;
                    if (FindByPrefix(ips.Properties, "InventoryContent_")?.Property is not ArrayProperty content
                        || content.Value is null) continue;
                    for (var j = 0; j < content.Value.Length; j++)
                    {
                        if (content.Value.GetValue(j) is not StructProperty slotSp || slotSp.Value is not PropertiesStruct slotPs) continue;
                        foreach (var f in slotPs.Properties) slotFields.Add(StripHash(f.Name.Value));
                        if (FindByPrefix(slotPs.Properties, "ChangeableData_")?.Property is StructProperty cSp
                            && cSp.Value is PropertiesStruct cPs)
                        {
                            foreach (var f in cPs.Properties) slotFields.Add("ChangeableData." + StripHash(f.Name.Value));
                        }
                    }
                }
            }
        }
        _output.WriteLine($"--- inventory slot field census ---");
        foreach (var f in slotFields) _output.WriteLine($"  {f}");
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

    // ---------- shared ----------

    /// <summary>Strips the blueprint hash suffix: <c>Hunger_2_A6C5...CA</c> -> <c>Hunger</c>.</summary>
    private static string StripHash(string name)
        => Regex.Replace(name, "_\\d+_[0-9A-F]{32}$", "");

    // Local clones of the Core readers' internal helpers (not InternalsVisibleTo here).

    private static FPropertyTag? FindByPrefix(IEnumerable<FPropertyTag> tags, string prefix)
        => tags.FirstOrDefault(t => t.Name?.Value is { } n && n.StartsWith(prefix, StringComparison.Ordinal));

    private static IList<KeyValuePair<FProperty, FProperty>>? GetMapPairs(IList<FPropertyTag>? topLevel, string namePrefix)
        => topLevel is not null && FindByPrefix(topLevel, namePrefix)?.Property is MapProperty mp ? mp.Value : null;

    private static IList<FPropertyTag> GetCharacterSaveData(SaveGame save)
    {
        var top = FindByPrefix(save.Properties!, "CharacterSaveData")
            ?? throw new InvalidDataException("Player save is missing the CharacterSaveData property.");
        if (top.Property is not StructProperty sp || sp.Value is not PropertiesStruct ps)
            throw new InvalidDataException("CharacterSaveData was not a struct of properties.");
        return ps.Properties;
    }
}
