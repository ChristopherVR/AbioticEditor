using AbioticEditor.Core.GamePass;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The editor-side half of the Game Pass safety work: what the screens are allowed to offer a
/// player whose world has gone wrong, and what the save path does when a write is refused rather
/// than failed.
/// </summary>
/// <remarks>
/// The Core operations themselves are covered by <see cref="GamePassGuardTests"/>. What is tested
/// here is the layer between them and the screens: that nothing is offered when there is nothing to
/// offer, that a leftover Core declined to name is not offered either, and that a restore leaves
/// the editor showing the world that is now on disk rather than the one it unpacked beforehand.
/// </remarks>
public sealed class GamePassRecoveryServiceTests
{
    // ---- nothing to rescue -----------------------------------------------------------------

    [SkippableFact]
    public void An_ordinary_world_offers_no_rescue_at_all()
    {
        using var workspace = NewWorkspace();

        // Nothing is open, so there is no folder to look in. A screen asking anyway must get empty
        // lists rather than an exception: the panel renders on every workspace, Game Pass or not.
        Assert.Empty(GamePassRecovery.Backups(workspace));
        Assert.Empty(GamePassRecovery.Orphans(workspace));
        Assert.False(GamePassRecovery.RepairIsTheRemedy(workspace));
        Assert.Null(workspace.GamePassWriteState());
    }

    [SkippableFact]
    public async Task A_steam_world_is_not_offered_game_pass_rescue()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        using var world = TempCopy.Of(Fixtures.CascadeDir!);
        using var workspace = NewWorkspace();

        await workspace.OpenAsync(world.Path);

        Assert.Empty(GamePassRecovery.Backups(workspace));
        Assert.Empty(GamePassRecovery.Orphans(workspace));
        // No Game Pass save is open, so there is no write state to report and nothing to insist on.
        Assert.Null(workspace.GamePassWriteState());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => workspace.SaveSelectedAcceptingGamePassRiskAsync("test"));
    }

    // ---- what a leftover is allowed to offer -----------------------------------------------

    [SkippableFact]
    public void A_leftover_core_declined_to_name_is_not_offered_a_button()
    {
        // Core withholds a name when the data does not say which world it is, and when a live world
        // already holds that name. Inventing one would register a container whose name disagrees
        // with the world inside it, so the screen must take "no name" as "no button".
        Assert.False(GamePassRecovery.CanPutBack(Leftover(world: null, suggested: null)));
        Assert.False(GamePassRecovery.CanPutBack(Leftover(world: "Cascade", suggested: null)));
        Assert.True(GamePassRecovery.CanPutBack(Leftover(world: "Cascade", suggested: "Cascade-WC")));
    }

    [SkippableTheory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(3L * 1024 * 1024, "3.0 MB")]
    public void Sizes_read_the_same_here_as_in_the_save_list(long bytes, string expected)
        => Assert.Equal(expected, GamePassRecovery.FormatSize(bytes));

    // ---- a real Game Pass folder -----------------------------------------------------------

    [SkippableFact]
    public async Task The_games_spare_copy_is_offered_and_restoring_it_reopens_the_world()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-recovery-restore-");
        var wgs = BuildGamePassWorld(scratch.Path, "Cascade");
        // The game's own spare copy of that world, one generation behind. Its contents do not have
        // to differ for this test: what matters is that it is offered, and that restoring it leaves
        // the editor reading from the folder afterwards rather than from a stale working copy.
        var spare = ReadWorldBlob(wgs, "Cascade-WC");
        WgsContainerStore.Open(wgs).AddOrReplaceContainer("Cascade-WC-B", spare);

        using var workspace = NewWorkspace();
        await workspace.OpenGamePassAsync(wgs, "Cascade-WC", source: null);

        var backup = Assert.Single(GamePassRecovery.Backups(workspace));
        Assert.Equal("Cascade", backup.WorldName);
        Assert.True(backup.LiveWorldExists);

        var restored = await GamePassRecovery.RestoreAsync(workspace, backup);

        Assert.Equal("Cascade", restored);
        // Reopened, not left pointing at the copy that was unpacked before the restore: the next
        // SAVE packs the working copy back, so a stale one would put the broken world straight back.
        Assert.NotNull(workspace.Current?.GamePass);
        Assert.Equal("Cascade-WC", workspace.Current!.GamePass!.Container);
        Assert.NotEmpty(workspace.Current.Saves);
        Assert.Equal(spare, ReadWorldBlob(wgs, "Cascade-WC"));
        // The whole folder is copied before a restore, which is what makes it reversible.
        Assert.NotEmpty(Directory.GetDirectories(
            Path.GetDirectoryName(wgs)!, Path.GetFileName(wgs) + ".bak*"));
    }

    [SkippableFact]
    public async Task A_healthy_game_pass_world_is_reported_as_writable_and_needs_no_repair()
    {
        Skip.IfNot(Fixtures.CascadeDir is not null, "the Steam world fixture is not in this checkout");
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");

        using var scratch = TempCopy.Empty("gp-recovery-state-");
        var wgs = BuildGamePassWorld(scratch.Path, "Cascade");
        using var workspace = NewWorkspace();
        await workspace.OpenGamePassAsync(wgs, "Cascade-WC", source: null);

        var state = workspace.GamePassWriteState();

        Assert.NotNull(state);
        // A scratch folder is not inside Connected Storage, so the game being open on the machine
        // running the tests says nothing about it and must not refuse the write.
        Assert.True(state!.CanWrite);
        Assert.Empty(state.Blockers);
        Assert.False(GamePassRecovery.RepairIsTheRemedy(workspace));
        Assert.Empty(GamePassRecovery.Orphans(workspace));
    }

    // ---- the copy that goes with it --------------------------------------------------------

    /// <summary>
    /// Every language says all of this in its own words. A player whose world has vanished is
    /// already having a bad day; being handed half an explanation in a language they do not read is
    /// not a small blemish, it is the moment they give up and delete something.
    /// </summary>
    [SkippableTheory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("ru")]
    public void Every_language_has_the_safety_and_rescue_copy(string language)
    {
        foreach (var key in SafetyAndRescueKeys)
        {
            var text = HostLanguageService.ResourceFor(language, key);
            // ResourceFor hands back the key itself when nothing is translated, so an untranslated
            // string would otherwise show up on screen as "Main_GpRestoreBackedUp".
            Assert.NotEqual(key, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    [SkippableTheory]
    [InlineData("Main_GpBackupMeta", 2)]
    [InlineData("Main_GpOrphanMeta", 2)]
    [InlineData("Main_GpRestoreTitle", 1)]
    [InlineData("Main_GpRestoreMessage", 1)]
    [InlineData("Main_GpRestored", 1)]
    [InlineData("Main_GpRecoverTitle", 1)]
    [InlineData("Main_GpRecoverMessage", 1)]
    [InlineData("Main_GpRecovered", 1)]
    public void The_rescue_copy_keeps_the_names_and_dates_it_is_given(string key, int placeholders)
    {
        // A translation that drops a placeholder does not fail loudly: it silently stops naming the
        // world, and every row on the rescue panel reads identically.
        foreach (var language in new[] { "en", "es", "fr", "de", "ru" })
        {
            var text = HostLanguageService.ResourceFor(language, key);
            for (var index = 0; index < placeholders; index++)
            {
                Assert.Contains(
                    "{" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                    text,
                    StringComparison.Ordinal);
            }
        }
    }

    private static readonly string[] SafetyAndRescueKeys =
    [
        "Main_GpSaveRefusedTitle", "Main_GpSaveRefusedMessage", "Main_GpSaveRefusedSafe",
        "Main_GpSaveAnywayHint", "Main_GpSaveAnyway", "Main_GpSavedAnyway", "Main_GpRepairThenSave",
        "Main_GpRepairFailed", "Main_GpNotWritableTitle", "Main_GpNotWritableMessage",
        "Main_GpNotWritableFix", "Main_GpRecoveryTitle", "Main_GpBackupsHeading", "Main_GpBackupsHint",
        "Main_GpBackupMeta", "Main_GpBackupOnlyCopy", "Main_GpRestoreBackup", "Main_GpRestoreTitle",
        "Main_GpRestoreMessage", "Main_GpRestoreBehind", "Main_GpRestoreBackedUp",
        "Main_GpRestoreConfirm", "Main_GpRestored", "Main_GpRestoreFailed", "Main_GpOrphansHeading",
        "Main_GpOrphansHint", "Main_GpOrphanMeta", "Main_GpOrphanNameTaken",
        "Main_GpOrphanUnknownWorld", "Main_GpRecoverOrphan", "Main_GpRecoverTitle",
        "Main_GpRecoverMessage", "Main_GpRecoverBackedUp", "Main_GpRecoverConfirm", "Main_GpRecovered",
        "Main_GpRecoverFailed", "Main_GpUnknownWorld", "Main_GpWhenUnknown",
    ];

    // ---- helpers ---------------------------------------------------------------------------

    private static SaveWorkspaceSessionService NewWorkspace()
        => new(new RecipeVocabularyService(), new ItemUpgradeVocabularyService(),
            new ProgressionVocabularyService(), new CodexVocabularyService(), new DesktopSaveFileSystem());

    private static WgsOrphanedContainer Leftover(string? world, string? suggested)
        => new("0123456789ABCDEF0123456789ABCDEF", @"C:\nowhere", 1, Guid.Empty, 1024,
            DateTime.UtcNow, world, suggested);

    /// <summary>A one-world Game Pass folder built out of the Steam fixture, so no personal Xbox
    /// data has to live in the repository.</summary>
    private static string BuildGamePassWorld(string root, string world)
    {
        var steam = Path.Combine(root, world);
        Directory.CreateDirectory(Path.Combine(steam, "PlayerData"));
        File.Copy(Path.Combine(Fixtures.CascadeDir!, "WorldSave_MetaData.sav"),
            Path.Combine(steam, "WorldSave_MetaData.sav"));
        var player = Directory.EnumerateFiles(
            Path.Combine(Fixtures.CascadeDir!, "PlayerData"), "Player_*.sav").First();
        File.Copy(player, Path.Combine(steam, "PlayerData", Path.GetFileName(player)));
        return GamePassConverter.SteamWorldToGamePass(steam, Path.Combine(root, "wgs"), worldName: world);
    }

    private static byte[] ReadWorldBlob(string wgsFolder, string container)
    {
        var store = WgsContainerStore.Open(wgsFolder);
        return store.ReadBlob(store.Find(container)!);
    }

    private sealed class TempCopy : IDisposable
    {
        private readonly DirectoryInfo _dir;

        private TempCopy(DirectoryInfo dir) => _dir = dir;

        public string Path => _dir.FullName;

        public static TempCopy Empty(string prefix) => new(Directory.CreateTempSubdirectory(prefix));

        public static TempCopy Of(string sourceRoot)
        {
            var copy = Empty("gp-recovery-world-");
            foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(copy.Path, System.IO.Path.GetRelativePath(sourceRoot, source));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            return copy;
        }

        public void Dispose()
        {
            try { _dir.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
