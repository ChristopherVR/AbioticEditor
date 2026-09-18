using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

/// <summary>
/// The very first thing a brand-new install shows: a language picker, then "what do you want to
/// do" (<c>ModeSelectDialog</c>), and nothing else - no save discovery, no Steam/Game Pass scan,
/// no world folder scan - starts loading in the background until the player has answered both.
/// </summary>
/// <remarks>
/// <para>"First run" is detected the same way the original MAUI app's own first-run language
/// prompt did (<c>LocalizationService.HasChosenLanguage</c>): nothing has been written to
/// <see cref="HostPreferenceStore"/>'s language key yet. No separate "first run completed" marker
/// file exists - the language choice itself is that marker, on both hosts (the browser keeps the
/// same key in <c>localStorage</c>, see <c>Program.UseBrowserStorageForPreferences</c>).</para>
///
/// <para>The behavioural half (nothing loads in the background until the prompt is answered) is
/// asserted the same way <c>LiveEditingBrowserHintTests</c> and <c>OpenGuardTests</c> assert their
/// own screen-wiring contracts: against the real source text of <c>MainLayout.razor</c> and
/// <c>ModeSelectDialog.razor</c>, since a headless Blazor render is not available to these tests.
/// Not run this round (a build would rewrite files the running app under test is currently
/// serving) - every identifier was cross-checked against the current source by hand instead.</para>
/// </remarks>
public sealed class FirstRunTests
{
    // ---- HostLanguageService.HasChosenLanguage: the first-run signal itself -----------------

    [Fact]
    public void A_fresh_profile_with_no_stored_language_is_a_first_run()
    {
        // The store writes to a real per-user file when no HostPreferenceStore.UseStore has been
        // installed (the desktop host's own default - see that type's remarks); preserve and
        // restore it so this test is hermetic, matching ReleaseNotesTests' own idiom for the same
        // kind of real per-user file.
        using var backup = new ConfigFileBackup(HostLanguageService.ConfigPath);
        backup.Delete();

        var languages = new HostLanguageService();

        Assert.False(languages.HasChosenLanguage);
    }

    [Fact]
    public void Saving_a_language_ends_the_first_run_for_this_and_every_later_instance()
    {
        using var backup = new ConfigFileBackup(HostLanguageService.ConfigPath);
        backup.Delete();

        var languages = new HostLanguageService();
        Assert.False(languages.HasChosenLanguage);

        languages.SetLanguage("es");

        Assert.True(languages.HasChosenLanguage);
        // A second instance reading the same file (mirroring a later app launch reading what the
        // first one wrote) must agree - HasChosenLanguage is read fresh each time, not cached.
        Assert.True(new HostLanguageService().HasChosenLanguage);
    }

    // ---- MainLayout: nothing under @Body starts before the prompt is answered ----------------

    [Fact]
    public void MainLayout_computes_first_run_from_HasChosenLanguage()
    {
        var source = Flatten(SharedSource("Components", "Pages", "MainLayout.razor"));

        Assert.Contains("_firstRun = !Languages.HasChosenLanguage;", source, StringComparison.Ordinal);
        Assert.Contains("_firstRunPending = _firstRun;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainLayout_never_renders_Body_while_a_first_run_is_pending()
    {
        var source = Flatten(SharedSource("Components", "Pages", "MainLayout.razor"));

        // @Body must sit behind an @if keyed on _firstRunPending being false - not merely styled
        // inert - so the routed page (Home.razor, with its Library.DiscoverAsync Steam/Game Pass
        // scan) is never instantiated at all until the prompt is answered.
        Assert.Contains(
            "@if (!_firstRunPending) { <WorkspaceShell><AppErrorBoundary>@Body</AppErrorBoundary></WorkspaceShell> }",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainLayout_opens_the_mode_select_dialog_on_a_first_run_on_every_host()
    {
        var source = Flatten(SharedSource("Components", "Pages", "MainLayout.razor"));

        // Not gated on _liveEditingAvailable alone (that is desktop-only) - a first run on the
        // browser host, which otherwise never auto-opens this dialog, must still open it so the
        // language step and the mode choice both happen before anything loads.
        Assert.Contains("if (_liveEditingAvailable || _firstRun) _modeSelectOpen = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainLayout_passes_the_first_run_flag_through_to_the_dialogs_language_step()
    {
        var source = Flatten(SharedSource("Components", "Pages", "MainLayout.razor"));

        Assert.Contains(
            "<ModeSelectDialog OnClose=\"CloseModeSelect\" AllowClose=\"@ModeSelectAllowClose\" ShowLanguageStep=\"@_firstRunPending\" />",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseModeSelect_permanently_clears_first_run_pending_so_a_later_reopen_never_blocks_Body_again()
    {
        var source = Flatten(SharedSource("Components", "Pages", "MainLayout.razor"));
        var start = source.IndexOf("private void CloseModeSelect()", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find CloseModeSelect in MainLayout.razor.");

        var closingBrace = source.IndexOf("_firstRunPending = false;", start, StringComparison.Ordinal);
        Assert.True(closingBrace > start, "CloseModeSelect must clear _firstRunPending.");
    }

    // ---- ModeSelectDialog: the language step itself -------------------------------------------

    [Fact]
    public void ModeSelectDialog_accepts_a_show_language_step_parameter_and_opens_on_it()
    {
        var source = Flatten(SharedSource("Components", "Shared", "ModeSelectDialog.razor"));

        Assert.Contains("[Parameter] public bool ShowLanguageStep { get; set; }", source, StringComparison.Ordinal);
        Assert.Contains(
            "protected override void OnInitialized() { if (!ShowLanguageStep) return; _step = Step.Language; _selectedLanguage = L.CurrentCode; }",
            source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_language_step_lists_every_available_language_by_its_own_name_and_applies_it_live()
    {
        var source = Flatten(SharedSource("Components", "Shared", "ModeSelectDialog.razor"));

        Assert.Contains("@foreach (var language in L.Available)", source, StringComparison.Ordinal);
        Assert.Contains("@language.NativeName", source, StringComparison.Ordinal);
        // Applied through the same setter the dedicated /language page uses (Language.razor), not
        // a second copy of the persistence logic.
        Assert.Contains("private void SelectLanguage(string code) { _selectedLanguage = code; L.SetLanguage(code); }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_language_step_has_no_close_button_and_continues_into_the_ordinary_Choose_step()
    {
        var source = Flatten(SharedSource("Components", "Shared", "ModeSelectDialog.razor"));

        // The close (X) button is gated on AllowClose for every step, including Language - it is
        // never rendered unconditionally inside the Language branch itself.
        var languageStepStart = source.IndexOf("_step == Step.Language", StringComparison.Ordinal);
        var chooseStepStart = source.IndexOf("_step == Step.Choose", StringComparison.Ordinal);
        Assert.True(languageStepStart >= 0 && chooseStepStart > languageStepStart);

        Assert.Contains("_step = Step.Choose;", source, StringComparison.Ordinal);
    }

    /// <summary>Whitespace-insensitive source text, so a re-wrap does not fail these.</summary>
    private static string Flatten(string source)
        => string.Join(' ', source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string SharedSource(params string[] parts) => UiSource.ReadAllText(parts);

    /// <summary>Backs up a real per-user preference file around a test and restores it afterward
    /// (mirroring <c>ReleaseNotesTests</c>' own inline idiom for <c>ReleaseNotesStore.ConfigPath</c>),
    /// so exercising <see cref="HostLanguageService"/>'s file-backed default never leaks a test's
    /// language choice into this machine's real settings or into another test.</summary>
    private sealed class ConfigFileBackup : IDisposable
    {
        private readonly string _path;
        private readonly bool _hadFile;
        private readonly string? _original;

        public ConfigFileBackup(string path)
        {
            _path = path;
            _hadFile = File.Exists(path);
            _original = _hadFile ? File.ReadAllText(path) : null;
        }

        /// <summary>Removes the file so the test starts from a genuinely fresh profile.</summary>
        public void Delete()
        {
            if (File.Exists(_path)) File.Delete(_path);
        }

        public void Dispose()
        {
            if (_original is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, _original);
            }
            else if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
