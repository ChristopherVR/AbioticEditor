using Microsoft.Extensions.DependencyInjection;

namespace AbioticEditor.App;

public partial class App : Application
{
	/// <summary>
	/// The one editor view-model, shared across page rebuilds. A theme switch replaces
	/// the whole Shell to re-resolve inline StaticResource colors; reusing this instance
	/// keeps the loaded folder, open save and any staged edits intact across that swap.
	/// </summary>
	public static ViewModels.MainViewModel SharedViewModel { get; } = new();

	public App()
	{
		// WinUI swallows unhandled exceptions (the process just exits) - keep a
		// last-chance crash log next to the user's temp folder for diagnosis.
		AppDomain.CurrentDomain.UnhandledException += (_, e) =>
			WriteCrashLog(e.ExceptionObject as Exception, "AppDomain");
		TaskScheduler.UnobservedTaskException += (_, e) =>
			WriteCrashLog(e.Exception, "UnobservedTask");

		// Finish any update file-swaps deferred from a previous install and sweep leftover
		// backups before anything else touches the install folder. Silent when idle.
		Services.UpdateService.RunStartupCleanup();

		InitializeComponent();
		// Resource dictionaries exist now; swap in the persisted palette before any
		// page resolves its StaticResource colors.
		Services.ThemeService.ApplyPersisted();

		// Apply the saved UI language, or - on first run - the OS default, before any page
		// resolves its localized strings. The first-run language prompt is shown from MainPage.
		Services.LocalizationService.ApplyStartup();

		// Install the app UI bridge (so plugins can drive the app), then discover and load
		// plugins once at startup. Never throws - a broken plugin is recorded and skipped,
		// the app comes up regardless.
		Services.PluginService.InstallHostUi(SharedViewModel, () => SharedViewModel.ReloadSelectedSaveAsync());
		Services.PluginService.Initialize();
	}

	/// <summary>
	/// Replaces the window's root page with a fresh <see cref="AppShell"/> on the shared
	/// view-model. Used to re-resolve inline StaticResource colours after a theme switch and to
	/// re-localize the whole UI after a language change (the <c>{loc:Localize}</c> bindings only
	/// pick up the new culture on a fresh page tree). The shared view-model is reused, so the open
	/// folder, save and staged edits survive the swap.
	/// </summary>
	public static void RebuildRootPage()
	{
		if (Current?.Windows is { Count: > 0 } windows)
		{
			windows[0].Page = new AppShell();
		}
	}

	private static void WriteCrashLog(Exception? ex, string source)
	{
		try
		{
			var path = Path.Combine(Path.GetTempPath(), "AbioticEditor-crash.log");
			File.AppendAllText(path, $"[{DateTime.Now:O}] {source}: {ex}\n\n");
		}
		catch (IOException)
		{
			// Crash logging must never crash.
		}
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell());
	}
}