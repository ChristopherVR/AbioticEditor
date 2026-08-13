using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The two questions asked before a world opens, and the promise the screens rely on: exactly one
/// answer comes back, whichever way the player replies.
/// </summary>
/// <remarks>
/// <para>Both guards hand control straight back to the caller the moment their dialog is on
/// screen, and the open then happens later through the dialog. A screen that disables its buttons
/// for the duration therefore cannot learn from the returned task that the attempt is over: it has
/// to be told. Told on the "yes" path only, every declined open left the page's buttons dead for
/// the rest of the session; told on neither, spamming OPEN started an open per click and the home
/// page flickered between the results as they landed one after another.</para>
///
/// <para>The cloud-sync warning has a second promise of its own: backing out of it must open
/// nothing AND must not count as having read it, or the very next attempt walks straight into the
/// save with no warning at all.</para>
/// </remarks>
public sealed class OpenGuardTests
{
    // ---- unsaved-edits question ------------------------------------------------------------

    [Fact]
    public async Task With_nothing_staged_the_action_runs_at_once_and_nothing_is_asked()
    {
        using var host = new GuardHost();
        var ran = 0;
        var declined = 0;

        await host.Unsaved.ConfirmAsync(() => { ran++; return Task.CompletedTask; }, () => { declined++; return Task.CompletedTask; });

        Assert.Equal(1, ran);
        Assert.Equal(0, declined);
        Assert.Null(host.Modals.Current);
    }

    [SkippableFact]
    public async Task Staying_put_reports_back_so_the_screen_can_free_its_buttons()
    {
        Skip.If(Fixtures.CascadeDir is null, "The Cascade world fixture is not present.");
        using var host = new GuardHost();
        using var world = TempWorld.CopyCascade();
        await host.StageAnEditAsync(world.Path);
        var ran = 0;
        var declined = 0;

        await host.Unsaved.ConfirmAsync(() => { ran++; return Task.CompletedTask; }, () => { declined++; return Task.CompletedTask; });

        var asked = Assert.IsType<ModalRequest>(host.Modals.Current);
        Assert.Equal(0, ran);

        await host.CancelAsync();

        Assert.Equal(0, ran);
        Assert.Equal(1, declined);
        Assert.NotNull(asked.OnCancel);
    }

    [SkippableFact]
    public async Task Carrying_on_runs_the_action_and_never_the_refusal()
    {
        Skip.If(Fixtures.CascadeDir is null, "The Cascade world fixture is not present.");
        using var host = new GuardHost();
        using var world = TempWorld.CopyCascade();
        await host.StageAnEditAsync(world.Path);
        var ran = 0;
        var declined = 0;

        await host.Unsaved.ConfirmAsync(() => { ran++; return Task.CompletedTask; }, () => { declined++; return Task.CompletedTask; });
        await host.ConfirmAsync();

        Assert.Equal(1, ran);
        Assert.Equal(0, declined);
    }

    // ---- Game Pass cloud-sync warning ------------------------------------------------------

    [Fact]
    public async Task An_ordinary_save_folder_opens_with_no_warning_at_all()
    {
        using var host = new GuardHost();
        using var folder = new ScratchFolder();
        var opened = 0;

        await host.GamePass.OpenAsync(folder.Path, () => { opened++; return Task.CompletedTask; }, () => Task.CompletedTask);

        Assert.Equal(1, opened);
        Assert.Null(host.Modals.Current);
    }

    [Fact]
    public async Task Backing_out_of_the_cloud_sync_warning_opens_nothing_and_says_so()
    {
        using var host = new GuardHost();
        using var folder = ScratchFolder.WithGamePassSave();
        var opened = 0;
        var declined = 0;

        await host.GamePass.OpenAsync(
            folder.Path, () => { opened++; return Task.CompletedTask; }, () => { declined++; return Task.CompletedTask; });

        Assert.NotNull(host.Modals.Current);

        await host.CancelAsync();

        Assert.Equal(0, opened);
        Assert.Equal(1, declined);
    }

    [Fact]
    public async Task The_cloud_sync_warning_comes_back_after_it_was_backed_out_of()
    {
        using var host = new GuardHost();
        using var folder = ScratchFolder.WithGamePassSave();

        await host.GamePass.OpenAsync(folder.Path, () => Task.CompletedTask, () => Task.CompletedTask);
        await host.CancelAsync();

        var opened = 0;
        await host.GamePass.OpenAsync(folder.Path, () => { opened++; return Task.CompletedTask; }, () => Task.CompletedTask);

        Assert.NotNull(host.Modals.Current);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task The_cloud_sync_warning_is_not_repeated_once_it_has_been_read()
    {
        using var host = new GuardHost();
        using var folder = ScratchFolder.WithGamePassSave();
        var opened = 0;

        await host.GamePass.OpenAsync(folder.Path, () => { opened++; return Task.CompletedTask; });
        await host.ConfirmAsync();
        Assert.Equal(1, opened);

        await host.GamePass.OpenAsync(folder.Path, () => { opened++; return Task.CompletedTask; });

        Assert.Equal(2, opened);
        Assert.Null(host.Modals.Current);
    }

    // ---- the screens' side of the bargain ---------------------------------------------------

    /// <summary>
    /// Every screen that holds a button down for the whole of an open must hand the guards a way
    /// to say the player declined, or the button never comes back.
    /// </summary>
    [Theory]
    [InlineData("Components/Pages/Home.razor", "EndWorkAsync")]
    [InlineData("Components/Pages/MainLayout.razor", "DoneOpeningAsync")]
    public void Screens_that_open_a_world_are_told_when_the_player_declines(string page, string release)
    {
        var source = Flatten(UiSource.ReadAllText(page));

        Assert.True(
            source.Contains($"{release}), {release});", StringComparison.Ordinal),
            $"{page} must pass its release callback ('{release}') to BOTH UnsavedChangesGuard."
            + "ConfirmAsync and GamePassSafetyGuard.OpenAsync. Each of them returns as soon as its "
            + "question is on screen, so a screen that keeps its buttons disabled for the whole "
            + "attempt only learns the attempt ended through that callback. Passing it to neither "
            + "brings the button back while the question is still up, which is how one click on "
            + "OPEN became several overlapping opens; passing it to only one leaves the buttons "
            + "dead until the editor is restarted.");
    }

    [Fact]
    public void Switching_saves_in_the_sidebar_is_told_when_the_player_declines()
    {
        var source = UiSource.ReadAllText("Components/Shared/WorkspaceShell.razor");

        Assert.Contains("Unsaved.ConfirmAsync(() => OpenAsync(save), DoneSwitchingSaveAsync)", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A button that opens a chooser has to be held from the click, not from when the chooser
    /// closes. The chooser IS the wait, and CONVERT spent all of it live: every press put another
    /// chooser on screen and queued another conversion behind it.
    /// </summary>
    [Theory]
    [InlineData("Components/Pages/GamePass.razor", "ConvertAsync", "_busy = true;", "FolderPicker.PickFolderAsync")]
    [InlineData("Components/Pages/Home.razor", "PickFolderAsync", "BeginWork()", "FolderPicker.PickFolderAsync")]
    [InlineData("Components/Pages/Home.razor", "OpenBundleAsync", "BeginWork()", "FilePicker.PickFileAsync")]
    [InlineData("Components/Pages/MainLayout.razor", "PickFolderAsync", "_openingFolder = true;", "FolderPicker.PickFolderAsync")]
    [InlineData("Components/Pages/Compare.razor", "PickAsync", "_picking = true;", "FolderPicker.PickFolderAsync")]
    [InlineData("Components/Pages/CreateWorld.razor", "PickDestinationAsync", "_picking = true;", "FolderPicker.PickFolderAsync")]
    [InlineData("Components/Pages/Settings.razor", "PickGameFolderAsync", "_gameDataBusy = true;", "FolderPicker.PickFolderAsync")]
    [InlineData("Components/Pages/Settings.razor", "ImportMappingsAsync", "_gameDataBusy = true;", "FilePicker.PickFileAsync")]
    [InlineData("Components/Player/PlayerAppearanceEditor.razor", "OpenAppearanceFileAsync", "_opening = true;", "FilePicker.PickFileAsync")]
    public void A_chooser_is_only_opened_once_the_button_has_been_taken(
        string page, string method, string take, string chooser)
    {
        var source = Flatten(UiSource.ReadAllText(page));
        var start = source.IndexOf($"Task {method}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {method} in {page}.");

        var opensChooser = source.IndexOf(chooser, start, StringComparison.Ordinal);
        Assert.True(opensChooser > start, $"{method} in {page} no longer opens a chooser.");

        var takesTheButton = source.IndexOf(take, start, StringComparison.Ordinal);
        Assert.True(
            takesTheButton > start && takesTheButton < opensChooser,
            $"{method} in {page} must take its busy flag ('{take}') before it opens the chooser, "
            + "not after the chooser closes. While a chooser is open the button behind it is still "
            + "live, so every further click opens another one.");
    }

    /// <summary>Whitespace-insensitive source text, so a re-wrap does not fail these.</summary>
    private static string Flatten(string source)
        => string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---- helpers ----------------------------------------------------------------------------

    private sealed class GuardHost : IDisposable
    {
        public SaveWorkspaceSessionService Workspace { get; } = new(
            new RecipeVocabularyService(), new ProgressionVocabularyService(), new CodexVocabularyService(),
            new DesktopSaveFileSystem());
        public ModalService Modals { get; } = new();
        public HostLanguageService Language { get; } = new();
        public ToastService Toasts { get; } = new();
        public UnsavedChangesGuard Unsaved { get; }
        public GamePassSafetyGuard GamePass { get; }

        public GuardHost()
        {
            Unsaved = new UnsavedChangesGuard(Workspace, Modals, Language);
            GamePass = new GamePassSafetyGuard(Workspace, Modals, Language, Toasts);
        }

        /// <summary>Answers the open question the way ModalHost does when the player says no.</summary>
        public async Task CancelAsync()
        {
            var modal = Modals.Current;
            Modals.Close();
            if (modal?.OnCancel is { } declined) await declined();
        }

        /// <summary>Answers it the way ModalHost does when the player carries on.</summary>
        public async Task ConfirmAsync()
        {
            var modal = Modals.Current;
            if (modal?.OnConfirm is not { } confirm) return;
            await confirm();
            if (ReferenceEquals(Modals.Current, modal)) Modals.Close();
        }

        /// <summary>Leaves a real staged edit in the workspace, which is what makes the guard ask.</summary>
        public async Task StageAnEditAsync(string worldFolder)
        {
            var opened = await Workspace.OpenAsync(worldFolder);
            var player = opened.Saves.First(save => save.Kind == SaveDocumentKind.Player);
            var selected = await Workspace.SelectAsync(player.Path);
            var session = selected.PlayerSession!;
            session.Vitals.Money += 7;
            session.MarkChanged();
            Assert.True(Workspace.HasStagedEdits);
        }

        public void Dispose() => Workspace.Dispose();
    }

    /// <summary>A throwaway folder, optionally carrying enough of an Xbox save to be recognised.</summary>
    private sealed class ScratchFolder : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("abiotic-open-guard-");

        public string Path => System.IO.Path.Combine(_root.FullName, "wgs");

        public ScratchFolder() => Directory.CreateDirectory(Path);

        public static ScratchFolder WithGamePassSave()
        {
            var folder = new ScratchFolder();
            // A container list plus its data is all the editor looks for, and a healthy one means
            // the repair question below the warning stays out of the way.
            WgsContainerStore.WriteNewContainer(folder.Path, "World-WC", new byte[64]);
            return folder;
        }

        public void Dispose()
        {
            try { _root.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed class TempWorld(DirectoryInfo directory) : IDisposable
    {
        public string Path => directory.FullName;

        public static TempWorld CopyCascade()
        {
            var destination = Directory.CreateTempSubdirectory("abiotic-open-guard-world-");
            foreach (var source in Directory.EnumerateFiles(Fixtures.CascadeDir!, "*", SearchOption.AllDirectories))
            {
                var target = System.IO.Path.Combine(destination.FullName, System.IO.Path.GetRelativePath(Fixtures.CascadeDir!, source));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            return new TempWorld(destination);
        }

        public void Dispose()
        {
            try { directory.Delete(recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
