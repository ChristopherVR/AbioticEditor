using System.IO;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Save-version management: the <see cref="SaveVersionRegistry"/> table, the
/// <see cref="CompatibilityReport"/> produced when opening saves, and the forward-compat
/// guarantees (unknown flags round-trip and stay ungated; future versions parse and warn).
/// </summary>
public class CompatibilityRegistryTests
{
    // ---------- registry table sanity ----------

    [Fact]
    public void Registry_HasOneEntryPerKnownKind()
    {
        var kinds = SaveVersionRegistry.Entries.Select(e => e.Kind).ToList();
        Assert.Equal(kinds.Count, kinds.Distinct().Count());
        Assert.Contains(SaveKind.World, kinds);
        Assert.Contains(SaveKind.Metadata, kinds);
        Assert.Contains(SaveKind.Character, kinds);
        Assert.Contains(SaveKind.Customization, kinds);
        Assert.DoesNotContain(SaveKind.Unknown, kinds);
    }

    [Fact]
    public void Registry_EntriesAreInternallyConsistent()
    {
        foreach (var entry in SaveVersionRegistry.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(entry.ValidatedGameBuild));
            // Min and Max travel together, and Min <= Max.
            Assert.Equal(entry.MinKnownVersion is null, entry.MaxKnownVersion is null);
            if (entry.MinKnownVersion is int min && entry.MaxKnownVersion is int max)
            {
                Assert.True(min <= max, $"{entry.Kind}: min {min} > max {max}");
            }
            Assert.Equal(entry.MaxKnownVersion is not null, entry.HasVersionedHeader);
        }
    }

    [Fact]
    public void Registry_LegacyConstantsComeFromTheTable()
    {
        Assert.Equal(
            SaveVersionRegistry.Find(SaveKind.World)!.MaxKnownVersion,
            SaveCompatibility.KnownGoodWorldVersion);
        Assert.Equal(
            SaveVersionRegistry.Find(SaveKind.Character)!.MaxKnownVersion,
            SaveCompatibility.KnownGoodCharacterVersion);
        // World and metadata saves share the Abiotic_WorldSave_C header layout/version.
        Assert.Equal(
            SaveVersionRegistry.Find(SaveKind.World)!.MaxKnownVersion,
            SaveVersionRegistry.Find(SaveKind.Metadata)!.MaxKnownVersion);
    }

    [Theory]
    [InlineData("/Game/Blueprints/Saves/Abiotic_WorldSave.Abiotic_WorldSave_C", SaveKind.World)]
    [InlineData("/Game/Blueprints/Saves/Abiotic_WorldMetadataSave.Abiotic_WorldMetadataSave_C", SaveKind.Metadata)]
    [InlineData("/Game/Blueprints/Saves/Abiotic_CharacterSave.Abiotic_CharacterSave_C", SaveKind.Character)]
    [InlineData("/Game/Blueprints/Saves/Abiotic_CustomizationSave.Abiotic_CustomizationSave_C", SaveKind.Customization)]
    [InlineData("/Game/Blueprints/Saves/Abiotic_BrandNewSave.Abiotic_BrandNewSave_C", SaveKind.Unknown)]
    [InlineData(null, SaveKind.Unknown)]
    [InlineData("", SaveKind.Unknown)]
    public void KindOfClassPath_RecognizesEveryKind(string? classPath, SaveKind expected)
    {
        Assert.Equal(expected, SaveVersionRegistry.KindOfClassPath(classPath));
    }

    // ---------- severity rules ----------

    [Fact]
    public void Classify_AppliesTheSeverityModel()
    {
        var worldMax = SaveVersionRegistry.Find(SaveKind.World)!.MaxKnownVersion!.Value;

        // Exact: known kind, version in (or below) range, nothing unknown.
        Assert.Equal(CompatibilitySeverity.Exact, SaveVersionRegistry.Classify(SaveKind.World, worldMax, hasUnknownContent: false));
        Assert.Equal(CompatibilitySeverity.Exact, SaveVersionRegistry.Classify(SaveKind.World, worldMax - 1, hasUnknownContent: false));

        // NewerMinor: known version but unknown content present.
        Assert.Equal(CompatibilitySeverity.NewerMinor, SaveVersionRegistry.Classify(SaveKind.World, worldMax, hasUnknownContent: true));

        // NewerVersion: above anything validated (unknown content doesn't downgrade it).
        Assert.Equal(CompatibilitySeverity.NewerVersion, SaveVersionRegistry.Classify(SaveKind.World, worldMax + 1, hasUnknownContent: false));
        Assert.Equal(CompatibilitySeverity.NewerVersion, SaveVersionRegistry.Classify(SaveKind.World, worldMax + 1, hasUnknownContent: true));

        // Unknown: unrecognized kind, or a versioned kind whose version couldn't be read.
        Assert.Equal(CompatibilitySeverity.Unknown, SaveVersionRegistry.Classify(SaveKind.Unknown, null, hasUnknownContent: false));
        Assert.Equal(CompatibilitySeverity.Unknown, SaveVersionRegistry.Classify(SaveKind.World, null, hasUnknownContent: false));

        // Customization has no version header: presence of unknown content decides.
        Assert.Equal(CompatibilitySeverity.Exact, SaveVersionRegistry.Classify(SaveKind.Customization, null, hasUnknownContent: false));
        Assert.Equal(CompatibilitySeverity.NewerMinor, SaveVersionRegistry.Classify(SaveKind.Customization, null, hasUnknownContent: true));
    }

    // ---------- report generation against real fixtures ----------

    [Fact]
    public void FixtureWorldSave_ReportsValidatedVersion()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var (data, report) = CompatibilityAnalyzer.ReadWorldWithReport(
            Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility_Office1.sav"));

        Assert.Equal(SaveKind.World, report.Kind);
        Assert.Equal(SaveCompatibility.KnownGoodWorldVersion, report.VersionSeen);
        Assert.NotNull(report.Known);
        Assert.Empty(report.UnknownFlags);
        Assert.Null(report.UnknownStoryChapter);
        // Severity is Exact, or NewerMinor when the reader logged unmodeled keys,
        // never a version mismatch for the validated fixture.
        Assert.True(
            report.Severity is CompatibilitySeverity.Exact or CompatibilitySeverity.NewerMinor,
            $"unexpected severity {report.Severity}: {report.Warning}");
        Assert.Equal(report.Severity == CompatibilitySeverity.NewerMinor, report.HasUnknownContent);
        Assert.NotNull(data.Raw.Properties);
        Assert.Contains("World save", report.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void FixtureMetadataSave_ReportsMetadataKind_AndKnownChapter()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var (data, report) = CompatibilityAnalyzer.ReadWorldWithReport(
            Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"));

        Assert.Equal(SaveKind.Metadata, report.Kind);
        Assert.Equal(SaveCompatibility.KnownGoodWorldVersion, report.VersionSeen);
        Assert.Null(report.UnknownStoryChapter);
        Assert.NotNull(data.StoryProgressionRow);
        Assert.True(report.Severity is CompatibilitySeverity.Exact or CompatibilitySeverity.NewerMinor);
    }

    [Fact]
    public void FixturePlayerSave_ReportsCharacterKind()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var (data, report) = CompatibilityAnalyzer.ReadPlayerWithReport(
            Path.Combine(Fixtures.CascadeDir!, "PlayerData", "Player_76561197993781479.sav"));

        Assert.Equal(SaveKind.Character, report.Kind);
        Assert.Equal(SaveCompatibility.KnownGoodCharacterVersion, report.VersionSeen);
        Assert.True(report.Severity is CompatibilitySeverity.Exact or CompatibilitySeverity.NewerMinor);
        Assert.True(data.Skills.Count > 0);
    }

    // ---------- synthetic future-version save ----------

    [Fact]
    public void FutureVersionSave_StillParses_AndReportsNewerVersion()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var temp = CreateTempDir();
        try
        {
            // WorldSave_Facility.sav is the fixture that carries the WorldFlags array.
            var path = Path.Combine(temp, "WorldSave_Facility.sav");
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav"), path);

            // Bump ABF_SAVE_VERSION through the public registry API (the save classes are internal).
            var bumped = SaveCompatibility.KnownGoodWorldVersion + 1;
            SaveGame save;
            using (var input = File.OpenRead(path))
            {
                save = SaveGame.LoadFrom(input);
            }
            Assert.True(SaveVersionRegistry.TrySetAbfVersion(save, bumped));
            using (var output = File.Create(path))
            {
                save.WriteTo(output);
            }

            // The reader must still parse the future-version save...
            var (data, report) = CompatibilityAnalyzer.ReadWorldWithReport(path);
            Assert.Equal(bumped, data.AbfVersion);
            Assert.True(data.Flags.Count > 0);

            // ...and the report must say "newer than this build".
            Assert.Equal(CompatibilitySeverity.NewerVersion, report.Severity);
            Assert.NotNull(report.Warning);
            Assert.Contains($"version {bumped}", report.Warning, StringComparison.Ordinal);
            Assert.Contains(".bak", report.Warning, StringComparison.Ordinal);
            Assert.NotNull(SaveCompatibility.WarningFor(data.Raw));

            // Writers don't refuse a parseable future save: a re-write must succeed and
            // keep the bumped version.
            WorldSaveWriter.WriteToFile(data, path);
            var reread = WorldSaveReader.ReadFromFile(path);
            Assert.Equal(bumped, reread.AbfVersion);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void CustomizationSave_HasNoVersionedHeader_ButKnownKind()
    {
        Assert.NotNull(Fixtures.ClientSavedDir);
        var path = Directory
            .EnumerateFiles(Fixtures.ClientSavedDir!, "ScientistCustomization_*.sav", SearchOption.AllDirectories)
            .FirstOrDefault();
        Assert.NotNull(path);

        SaveGame save;
        using (var input = File.OpenRead(path!))
        {
            save = SaveGame.LoadFrom(input);
        }

        // No registered versioned header: version is unreadable and unbumpable...
        Assert.Null(SaveVersionRegistry.GetAbfVersion(save));
        Assert.False(SaveVersionRegistry.TrySetAbfVersion(save, 42));

        // ...but the kind is still recognized and classifies as validated.
        var report = CompatibilityAnalyzer.Analyze(save);
        Assert.Equal(SaveKind.Customization, report.Kind);
        Assert.Equal(CompatibilitySeverity.Exact, report.Severity);
        Assert.Null(report.Warning);
    }

    // ---------- unknown quest flags ----------

    private const string FutureFlag = "ZZZFuture_FlagNotInAnyCatalog";

    [Fact]
    public void UnknownFlag_RoundTripsByteIdentical_AndSurfacesInReport()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var temp = CreateTempDir();
        try
        {
            // WorldSave_Facility.sav is the fixture that carries the WorldFlags array.
            var path = Path.Combine(temp, "WorldSave_Facility.sav");
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav"), path);

            // Inject a flag this build's QuestFlagCatalog has never heard of.
            var data = WorldSaveReader.ReadFromFile(path);
            Assert.True(data.Flags.Count > 0, "fixture should already carry world flags");
            Assert.DoesNotContain(FutureFlag, QuestFlagCatalog.KnownFlags);
            WorldSaveWriter.ApplyFlags(data, data.Flags.Append(FutureFlag).ToList());
            WorldSaveWriter.WriteToFile(data, path);

            // 1. The file containing the unknown flag round-trips byte-identical.
            var original = File.ReadAllBytes(path);
            SaveGame save;
            using (var input = new MemoryStream(original))
            {
                save = SaveGame.LoadFrom(input);
            }
            using var rewritten = new MemoryStream();
            save.WriteTo(rewritten);
            Assert.True(original.AsSpan().SequenceEqual(rewritten.ToArray()),
                "save containing an unknown flag must round-trip byte-identical");

            // 2. The reader preserves the flag and the report surfaces it.
            var (reread, report) = CompatibilityAnalyzer.ReadWorldWithReport(path);
            Assert.Contains(FutureFlag, reread.Flags);
            Assert.Contains(FutureFlag, report.UnknownFlags);
            Assert.Equal(CompatibilitySeverity.NewerMinor, report.Severity);
            Assert.NotNull(report.Warning);
            Assert.Contains("quest flag", report.Warning, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public void UnknownFlag_IsUngated_AndClassifiesWithoutThrowing()
    {
        // FlagGate: an unknown flag has no prerequisites (no prereq block in the UI)...
        Assert.Empty(FlagGate.PrerequisitesFor(FutureFlag));
        // ...and no region chapter mapping.
        Assert.Null(FlagGate.RegionChapterFor(FutureFlag));

        // QuestFlagCatalog still produces a usable (uncurated) classification.
        var info = QuestFlagCatalog.Lookup(FutureFlag);
        Assert.Equal(FutureFlag, info.Name);
        Assert.False(string.IsNullOrEmpty(info.FriendlyName));
    }

    // ---------- unknown story chapter ----------

    [Fact]
    public void UnknownChapter_LookupsAreNullNotThrowing()
    {
        Assert.Null(StoryProgressionCatalog.Find("Chapter_FromTheFuture"));
        Assert.Equal(-1, StoryProgressionCatalog.IndexOf("Chapter_FromTheFuture"));
        Assert.Null(StoryProgressionCatalog.ChapterForFlag(FutureFlag));
    }

    [Fact]
    public void UnknownChapter_SurfacesInMetadataReport()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var temp = CreateTempDir();
        try
        {
            var path = Path.Combine(temp, "WorldSave_MetaData.sav");
            File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"), path);

            var data = WorldSaveReader.ReadFromFile(path);
            WorldSaveWriter.ApplyStoryProgression(data, "Chapter_FromTheFuture");
            WorldSaveWriter.WriteToFile(data, path);

            var (reread, report) = CompatibilityAnalyzer.ReadWorldWithReport(path);
            Assert.Equal("Chapter_FromTheFuture", reread.StoryProgressionRow);
            Assert.Equal("Chapter_FromTheFuture", report.UnknownStoryChapter);
            Assert.Equal(CompatibilitySeverity.NewerMinor, report.Severity);
            Assert.NotNull(report.Warning);
            Assert.Contains("story chapter", report.Warning, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    // ---------- unknown-content collector (works with logging OFF) ----------

    [Fact]
    public void Collector_CapturesUnknownData_WithLoggingDisabled()
    {
        // No Enabled assertion here: EditorLogTests toggles that global flag from a
        // parallel test class, and the observer event fires before the Enabled gate
        // anyway - that pre-gate behavior is exactly what this test covers.

        // Unique keys so concurrent tests (which also pump the UNKWN channel) can't interfere.
        var enumKey = $"enum-{Guid.NewGuid():N}";
        var propKey = $"Prop_{Guid.NewGuid():N}";

        using var collector = UnknownContentCollector.Begin();
        EditorLog.UnknownData("DoorState", enumKey, "synthetic");
        EditorLog.UnknownData("DoorState", enumKey, "synthetic duplicate");
        EditorLog.UnknownData("WorldSave", propKey, "synthetic");

        // Deduplicated per (area, key); partitioned into enum values vs property keys.
        Assert.Equal(1, collector.Entries.Count(e => e.Key == enumKey));
        Assert.Contains($"DoorState: {enumKey}", collector.UnknownEnumValues);
        Assert.Contains($"WorldSave: {propKey}", collector.UnknownPropertyKeys);
        Assert.DoesNotContain($"WorldSave: {propKey}", collector.UnknownEnumValues);
    }

    [Fact]
    public void Collector_StopsObservingAfterDispose()
    {
        var key = $"enum-{Guid.NewGuid():N}";
        var collector = UnknownContentCollector.Begin();
        collector.Dispose();
        EditorLog.UnknownData("DoorState", key, null);
        Assert.DoesNotContain(collector.Entries, e => e.Key == key);
    }

    [Fact]
    public void CollectedUnknowns_FlowIntoTheReport()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var data = WorldSaveReader.ReadFromFile(
            Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility_Office1.sav"));

        using var collector = UnknownContentCollector.Begin();
        var enumKey = $"enum-{Guid.NewGuid():N}";
        EditorLog.UnknownData("EquipSlot", enumKey, "synthetic");

        var report = CompatibilityAnalyzer.AnalyzeWorld(data, collector);
        Assert.Contains($"EquipSlot: {enumKey}", report.UnknownEnumValues);
        Assert.True(report.HasUnknownContent);
        Assert.Equal(CompatibilitySeverity.NewerMinor, report.Severity);
    }

    // ---------- helpers ----------

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "abiotic-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
