using System.IO;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;

namespace AbioticEditor.Tests;

/// <summary>
/// Version evidence, operation-support verdicts and the fixture support matrix
/// (<c>docs/reference/compatibility-support-matrix.md</c>).
/// </summary>
public class CompatibilitySupportTests
{
    private static readonly string[] VitalsOnly = ["vitals"];

    private static string? FixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    public static IEnumerable<object[]> EveryGvasFixture()
    {
        var root = FixtureRoot();
        if (root is null) yield break;
        foreach (var path in Directory.EnumerateFiles(root, "*.sav", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            yield return new object[] { Path.GetRelativePath(root, path).Replace('\\', '/') };
        }
    }

    private static SaveGame LoadFixture(string relative)
    {
        var path = Path.Combine(FixtureRoot()!, relative);
        _ = Fixtures.CascadeDir; // forces save-class registration in the Fixtures static constructor
        return SaveGame.LoadFrom(new MemoryStream(File.ReadAllBytes(path)));
    }

    // ---------- fixture round trip (Steam, dedicated server) ----------

    [Theory]
    [MemberData(nameof(EveryGvasFixture))]
    public void Every_fixture_round_trips_byte_for_byte_and_has_identified_header(string relative)
    {
        Assert.NotNull(FixtureRoot());
        var original = File.ReadAllBytes(Path.Combine(FixtureRoot()!, relative));

        var save = LoadFixture(relative);
        using var output = new MemoryStream();
        save.WriteTo(output);
        Assert.True(original.AsSpan().SequenceEqual(output.ToArray()), $"{relative} did not round-trip byte for byte");

        var header = SaveHeaderEvidence.TryParse(original);
        Assert.NotNull(header);
        Assert.Equal("++DF+ABF", header!.EngineBranch);
        var identification = SaveVersionRegistry.IdentifyBuild(header, out _);
        Assert.True(
            identification is BuildIdentification.ValidatedEngineBuild or BuildIdentification.ObservedEngineBuild,
            $"{relative} has an engine build ({header.EngineLabel}) missing from the registry");

        // The header recovered from the loaded save must equal the one read from the bytes.
        Assert.Equal(header, SaveHeaderEvidence.FromLoaded(save));
    }

    [SkippableFact]
    public void Game_pass_fixture_members_round_trip_when_the_bundle_can_be_read()
    {
        Skip.If(Fixtures.GamePassWgsDir is null, "the Game Pass fixture is not in this checkout");

        GamePassSaveSet set;
        IReadOnlyList<GamePassSaveEntry> entries;
        try
        {
            set = GamePassSaveSet.Open(Fixtures.GamePassWgsDir!);
            entries = set.Entries();
        }
        catch (Exception ex)
        {
            Skip.If(true, $"the Game Pass bundle cannot be opened here (native Oodle?): {ex.Message}");
            return;
        }
        Skip.If(entries.Count == 0, "the Game Pass bundle could not be decoded here (native Oodle library missing?)");

        foreach (var entry in entries.Where(e => e.IsEditable))
        {
            var gvas = set.ReadSave(entry);
            var save = SaveGame.LoadFrom(new MemoryStream(gvas));
            using var output = new MemoryStream();
            save.WriteTo(output);
            Assert.True(gvas.AsSpan().SequenceEqual(output.ToArray()), $"{entry.FileName} did not round-trip");
            Assert.NotNull(SaveHeaderEvidence.TryParse(gvas));
        }
    }

    // ---------- version classification ----------

    [Fact]
    public void Classify_flags_versions_below_the_recorded_minimum()
    {
        var min = SaveVersionRegistry.Find(SaveKind.World)!.MinKnownVersion!.Value;
        Assert.Equal(CompatibilitySeverity.OlderVersion, SaveVersionRegistry.Classify(SaveKind.World, min - 1, false));
        Assert.Equal(CompatibilitySeverity.OlderVersion, SaveVersionRegistry.Classify(SaveKind.World, min - 1, true));
        Assert.Equal(CompatibilitySeverity.Exact, SaveVersionRegistry.Classify(SaveKind.World, min, false));
    }

    [SkippableFact]
    public void An_older_version_save_can_be_inspected_but_not_written()
    {
        Skip.If(Fixtures.ServerWorldsDir is null, "the dedicated-server fixture is not in this checkout");
        var path = Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_MetaData.sav");
        var data = WorldSaveReader.ReadFromFile(path);
        Assert.True(SaveVersionRegistry.TrySetAbfVersion(data.Raw, 2));

        var report = CompatibilityAnalyzer.AnalyzeWorld(data);

        Assert.Equal(CompatibilitySeverity.OlderVersion, report.Severity);
        Assert.NotNull(report.Warning);
        Assert.NotNull(SaveCompatibility.WarningFor(data.Raw));
        Assert.True(report.Operations.CanInspect);
        Assert.False(report.Operations.CanWrite(EditingArea.WorldStoryAndFlags));
        Assert.Contains(report.Operations.BlockedAreas, a => a.Area == EditingArea.WorldStoryAndFlags);
    }

    // ---------- build identification and support verdicts ----------

    [SkippableFact]
    public void Validated_build_metadata_save_is_supported_and_older_engine_build_is_unverified()
    {
        Skip.If(Fixtures.ServerWorldsDir is null || Fixtures.CascadeDir is null, "fixtures missing");

        var server = CompatibilityAnalyzer.AnalyzeWorld(WorldSaveReader.ReadFromFile(
            Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_MetaData.sav")));
        Assert.Equal(BuildIdentification.ValidatedEngineBuild, server.BuildIdentification);
        Assert.Equal(1030002, server.Header!.EngineChangelist);
        Assert.Equal(OperationSupportLevel.Supported, server.Operations.Get(EditingArea.WorldStoryAndFlags).Level);

        var legacy = CompatibilityAnalyzer.AnalyzeWorld(WorldSaveReader.ReadFromFile(
            Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility_DF_RadWaste.sav")));
        Assert.Equal(BuildIdentification.ObservedEngineBuild, legacy.BuildIdentification);
        Assert.Equal(1030001, legacy.Header!.EngineChangelist);
        var containers = legacy.Operations.Get(EditingArea.WorldContainers);
        Assert.Equal(OperationSupportLevel.Unverified, containers.Level);
        Assert.True(containers.AllowsWrite);
        Assert.True(legacy.Operations.CanInspect);
    }

    [SkippableFact]
    public void Unrecognized_engine_builds_are_unverified_or_unsupported_by_direction()
    {
        Skip.If(Fixtures.ServerWorldsDir is null, "the dedicated-server fixture is not in this checkout");
        var data = WorldSaveReader.ReadFromFile(Path.Combine(Fixtures.ServerWorldsDir!, "WorldSave_MetaData.sav"));
        var header = SaveHeaderEvidence.FromLoaded(data.Raw)!;

        var newer = CompatibilityAnalyzer.Analyze(data.Raw, null, null, null, header with { EngineChangelist = 2000000 });
        Assert.Equal(BuildIdentification.UnrecognizedEngineBuild, newer.BuildIdentification);
        Assert.Equal(OperationSupportLevel.Unverified, newer.Operations.Get(EditingArea.WorldStoryAndFlags).Level);

        var older = CompatibilityAnalyzer.Analyze(data.Raw, null, null, null, header with { EngineChangelist = 900000 });
        Assert.Equal(OperationSupportLevel.Unsupported, older.Operations.Get(EditingArea.WorldStoryAndFlags).Level);
        Assert.True(older.Operations.CanInspect);
    }

    [Fact]
    public void A_report_without_header_evidence_says_unknown_and_never_supported()
    {
        var report = new CompatibilityReport { Kind = SaveKind.Character, Severity = CompatibilitySeverity.Exact };
        Assert.Equal(BuildIdentification.Unknown, report.BuildIdentification);
        Assert.Equal(OperationSupportLevel.Unverified, report.Operations.Get(EditingArea.PlayerInventory).Level);
        Assert.Contains("game build not identified", report.Summary);
    }

    [Fact]
    public void Unknown_save_kind_blocks_every_write_area()
    {
        var report = new CompatibilityReport { Kind = SaveKind.Unknown, Severity = CompatibilitySeverity.Unknown };
        Assert.False(report.Operations.CanInspect);
        Assert.False(report.Operations.CanWrite(EditingArea.PlayerStats));
    }

    [Fact]
    public void Garbage_bytes_have_no_header_evidence()
    {
        Assert.Null(SaveHeaderEvidence.TryParse(new byte[] { 1, 2, 3, 4, 5 }));
        Assert.Null(SaveHeaderEvidence.TryParse(ReadOnlySpan<byte>.Empty));
    }

    [SkippableFact]
    public void Player_save_areas_follow_the_same_rules()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam fixture is not in this checkout");
        var path = Directory.EnumerateFiles(Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").Order(StringComparer.Ordinal).First();
        var report = CompatibilityAnalyzer.AnalyzePlayer(PlayerSaveReader.ReadFromFile(path));
        Assert.Equal(SaveKind.Character, report.Kind);
        Assert.NotNull(report.Operations.Find(EditingArea.PlayerIdentity));
        Assert.Null(report.Operations.Find(EditingArea.WorldContainers));
        Assert.False(report.Operations.CanWrite(EditingArea.WorldContainers));
    }

    // ---------- live compatibility ----------

    [Fact]
    public void Live_protocol_mismatch_is_unsupported_and_missing_facts_are_only_unverified()
    {
        var need = new LiveActionRequirement(1);

        Assert.Equal(OperationSupportLevel.Unsupported,
            LiveCompatibilityEvaluator.Evaluate(new LiveCompatibilityInfo(null, null, null, null, null), need).Level);
        Assert.Equal(OperationSupportLevel.Unsupported,
            LiveCompatibilityEvaluator.Evaluate(new LiveCompatibilityInfo(null, 2, "9", null, null), need).Level);

        var handshakeOnly = new LiveCompatibilityInfo(null, 1, "1.0", null, null);
        var verdict = LiveCompatibilityEvaluator.Evaluate(handshakeOnly, need);
        Assert.Equal(OperationSupportLevel.Unverified, verdict.Level);
        Assert.True(verdict.Enabled);

        Assert.Equal(OperationSupportLevel.Unverified,
            LiveCompatibilityEvaluator.Evaluate(handshakeOnly, new LiveActionRequirement(1, "power.socket")).Level);
        var withCaps = handshakeOnly with { AgentCapabilities = VitalsOnly };
        Assert.Equal(OperationSupportLevel.Unsupported,
            LiveCompatibilityEvaluator.Evaluate(withCaps, new LiveActionRequirement(1, "power.socket")).Level);

        var full = new LiveCompatibilityInfo(SaveVersionRegistry.ValidatedGameBuild, 1, "1.0", VitalsOnly, "3.0.1");
        Assert.Equal(OperationSupportLevel.Supported,
            LiveCompatibilityEvaluator.Evaluate(full, new LiveActionRequirement(1, "vitals")).Level);
        Assert.Equal(OperationSupportLevel.Unverified,
            LiveCompatibilityEvaluator.Evaluate(full with { GameBuild = "other" }, need).Level);
    }
}
