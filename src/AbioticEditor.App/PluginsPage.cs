using AbioticEditor.App.Services;
using AbioticEditor.App.ViewModels;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.App;

/// <summary>
/// The Plugins management sheet: lists every installed plugin with its status and an
/// enable/disable toggle, runs the save operations applicable to the open save through the
/// host's backup/write path, and opens plugin UI tools in a hosted page. Built in code (like
/// <see cref="SettingsPage"/>) so it picks up the current palette each time it opens.
/// </summary>
public sealed class PluginsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private readonly Func<Task> _rebuildHost;

    public PluginsPage(MainViewModel vm, Func<Task> rebuildHost)
    {
        _vm = vm;
        _rebuildHost = rebuildHost;
        Title = "Plugins";
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        Content = BuildContent();
    }

    private ScrollView BuildContent()
    {
        var accent = (Color)Application.Current!.Resources["AfAccentOrange"];
        var muted = (Color)Application.Current.Resources["AfTextSecondary"];

        Label Header(string text) => new()
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = 11,
            CharacterSpacing = 3,
            TextColor = accent,
            Margin = new Thickness(0, 14, 0, 2),
        };

        Label Hint(string text) => new() { Text = text, FontSize = 11, TextColor = muted };

        var root = new VerticalStackLayout
        {
            Padding = new Thickness(28, 24),
            Spacing = 10,
            MaximumWidthRequest = 620,
            Children =
            {
                new Label { Text = "PLUGINS", FontFamily = "OpenSansSemibold", FontSize = 20, CharacterSpacing = 2 },
                Hint("Plugins extend the editor with new save operations, commands, and panels. "
                    + "They run with full trust - only install plugins you trust. Drop plugin folders "
                    + $"under: {PluginPaths.UserPluginsDirectory}"),
            },
        };

        // ---- Installed plugins ----
        root.Children.Add(Header("INSTALLED"));
        var descriptors = PluginService.Descriptors;
        if (descriptors.Count == 0)
        {
            root.Children.Add(Hint("No plugins installed."));
        }
        else
        {
            foreach (var d in descriptors)
            {
                root.Children.Add(BuildPluginRow(d, muted));
            }
        }

        // ---- Save operations for the open save ----
        root.Children.Add(Header("SAVE OPERATIONS"));
        var openPath = _vm.SelectedSave?.FullPath;
        if (openPath is null)
        {
            root.Children.Add(Hint("Open a save to run save operations against it."));
        }
        else
        {
            var openKind = SaveKindDetector.Detect(openPath);
            var applicable = PluginService.SaveOperations
                .Where(c => SaveKindDetector.Matches(c.Value.AppliesTo, openKind))
                .ToList();
            root.Children.Add(Hint($"Open save: {Path.GetFileName(openPath)} ({openKind}). "
                + "Running an operation writes the file immediately (a .bak is kept) and reloads the editor, "
                + "discarding any unsaved edits."));
            if (applicable.Count == 0)
            {
                root.Children.Add(Hint("No installed operation applies to this save kind."));
            }
            else
            {
                foreach (var op in applicable)
                {
                    root.Children.Add(BuildOperationCard(op, openPath, muted));
                }
            }
        }

        // ---- UI tools ----
        root.Children.Add(Header("TOOLS"));
        var tools = PluginService.EditorTools;
        if (tools.Count == 0)
        {
            root.Children.Add(Hint("No UI tools installed."));
        }
        else
        {
            foreach (var tool in tools)
            {
                root.Children.Add(BuildToolRow(tool));
            }
        }

        // ---- Web (HTML/React) tools ----
        root.Children.Add(Header("WEB TOOLS"));
        var webTools = PluginService.WebTools;
        if (webTools.Count == 0)
        {
            root.Children.Add(Hint("No web tools installed."));
        }
        else
        {
            foreach (var web in webTools)
            {
                root.Children.Add(BuildWebToolRow(web));
            }
        }

        // ---- Menu actions ----
        root.Children.Add(Header("MENU ACTIONS"));
        var actions = PluginService.MenuActions;
        if (actions.Count == 0)
        {
            root.Children.Add(Hint("No menu actions installed."));
        }
        else
        {
            foreach (var action in actions)
            {
                root.Children.Add(BuildMenuActionRow(action));
            }
        }

        var close = new Button { Text = "CLOSE" };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        root.Children.Add(new BoxView { HeightRequest = 8, Color = Colors.Transparent });
        root.Children.Add(close);

        return new ScrollView { Content = root };
    }

    private View BuildPluginRow(PluginDescriptor d, Color muted)
    {
        var stack = new VerticalStackLayout { Spacing = 2 };
        var titleRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        titleRow.Add(new Label
        {
            Text = $"{d.Manifest.Name}  v{d.Manifest.Version}",
            FontFamily = "OpenSansSemibold",
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);

        var enableSwitch = new Switch { IsToggled = d.Manifest.Enabled, VerticalOptions = LayoutOptions.Center };
        enableSwitch.Toggled += async (_, e) =>
        {
            if (!d.SetEnabled(e.Value))
            {
                await DisplayAlertAsync("Plugins", "Could not update the plugin manifest (read-only folder?).", "OK");
                enableSwitch.IsToggled = d.Manifest.Enabled;
                return;
            }
            await DisplayAlertAsync("Plugins",
                $"{d.Manifest.Name} {(e.Value ? "enabled" : "disabled")}. Restart the app to apply.", "OK");
        };
        titleRow.Add(enableSwitch, 1, 0);
        stack.Children.Add(titleRow);

        var status = $"{d.State} · {d.CapabilitySummary()}";
        if (d.LoadError is not null)
        {
            status += $" · {d.LoadError}";
        }
        stack.Children.Add(new Label { Text = status, FontSize = 11, TextColor = muted });
        if (!string.IsNullOrWhiteSpace(d.Manifest.Description))
        {
            stack.Children.Add(new Label { Text = d.Manifest.Description, FontSize = 11, TextColor = muted });
        }

        return new Border
        {
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 2),
            Content = stack,
        };
    }

    private View BuildOperationCard(PluginCapability<ISaveOperation> capability, string savePath, Color muted)
    {
        var op = capability.Value;
        var stack = new VerticalStackLayout { Spacing = 6 };
        stack.Children.Add(new Label { Text = op.DisplayName, FontFamily = "OpenSansSemibold" });
        if (!string.IsNullOrWhiteSpace(op.Description))
        {
            stack.Children.Add(new Label { Text = op.Description, FontSize = 11, TextColor = muted });
        }

        // One entry per declared parameter, pre-filled with the default.
        var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in op.Parameters)
        {
            var entry = new Entry { Placeholder = p.Description, Text = p.DefaultValue ?? string.Empty };
            entries[p.Name] = entry;
            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(140), new ColumnDefinition(GridLength.Star) },
            };
            row.Add(new Label { Text = p.Name, FontSize = 12, VerticalOptions = LayoutOptions.Center }, 0, 0);
            row.Add(entry, 1, 0);
            stack.Children.Add(row);
        }

        var run = new Button { Text = "RUN" };
        run.Clicked += async (_, _) => await RunOperationAsync(capability, savePath, entries, run);
        stack.Children.Add(run);

        return new Border { Padding = new Thickness(12, 10), Margin = new Thickness(0, 2), Content = stack };
    }

    private async Task RunOperationAsync(
        PluginCapability<ISaveOperation> capability,
        string savePath,
        IReadOnlyDictionary<string, Entry> entries,
        Button run)
    {
        var op = capability.Value;
        var confirm = await DisplayAlertAsync(
            op.DisplayName,
            $"Run '{op.DisplayName}' on {Path.GetFileName(savePath)}?\n\n"
            + "This writes the file immediately (a .bak is kept) and reloads the editor, discarding unsaved edits.",
            "Run", "Cancel");
        if (!confirm)
        {
            return;
        }

        var parameters = entries.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        run.IsEnabled = false;
        try
        {
            var outcome = await PluginService.RunOperationAsync(capability, savePath, parameters, dryRun: false);
            if (!outcome.Result.Success)
            {
                await DisplayAlertAsync(op.DisplayName, $"Failed: {outcome.Result.Message}", "OK");
                return;
            }

            await DisplayAlertAsync(op.DisplayName,
                $"{outcome.Result.Message}\n\n{(outcome.Wrote ? "Save written (.bak kept). Reloading editor." : "No change was needed.")}",
                "OK");

            if (outcome.Wrote)
            {
                // Re-read from disk so the editor reflects the plugin's edits.
                await _vm.ReloadSelectedSaveAsync();
            }
        }
        finally
        {
            run.IsEnabled = true;
        }
    }

    private View BuildToolRow(PluginCapability<IEditorTool> capability)
    {
        var tool = capability.Value;
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 2),
        };
        var glyph = string.IsNullOrEmpty(tool.Glyph) ? string.Empty : $"{tool.Glyph}  ";
        row.Add(new Label
        {
            Text = $"{glyph}{tool.Title}",
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);

        var open = new Button { Text = "OPEN" };
        open.Clicked += async (_, _) => await OpenToolAsync(capability);
        row.Add(open, 1, 0);
        return row;
    }

    private View BuildWebToolRow(PluginCapability<IWebTool> capability)
    {
        var tool = capability.Value;
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 2),
        };
        var glyph = string.IsNullOrEmpty(tool.Glyph) ? string.Empty : $"{tool.Glyph}  ";
        row.Add(new Label { Text = $"{glyph}{tool.Title}", VerticalOptions = LayoutOptions.Center }, 0, 0);

        var open = new Button { Text = "OPEN" };
        open.Clicked += async (_, _) => await OpenWebToolAsync(capability);
        row.Add(open, 1, 0);
        return row;
    }

    private async Task OpenWebToolAsync(PluginCapability<IWebTool> capability)
    {
        try
        {
            var context = PluginService.CreateWebToolContext(capability, () => _vm.SelectedSave?.FullPath);
            await Navigation.PushModalAsync(new WebToolHostPage(capability, context));
        }
        catch (Exception ex)
        {
            capability.Plugin.Host?.Log.Error("web tool failed to open", ex);
            await DisplayAlertAsync(capability.Value.Title, $"The web tool failed to open: {ex.Message}", "OK");
        }
    }

    private View BuildMenuActionRow(PluginCapability<IMenuAction> capability)
    {
        var action = capability.Value;
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 2),
        };
        var glyph = string.IsNullOrEmpty(action.Glyph) ? string.Empty : $"{action.Glyph}  ";
        var label = action.Group is { Length: > 0 } g ? $"{glyph}{g} · {action.Title}" : $"{glyph}{action.Title}";
        row.Add(new Label { Text = label, VerticalOptions = LayoutOptions.Center }, 0, 0);

        var run = new Button { Text = "RUN" };
        run.Clicked += async (_, _) => await RunMenuActionAsync(capability);
        row.Add(run, 1, 0);
        return row;
    }

    private async Task RunMenuActionAsync(PluginCapability<IMenuAction> capability)
    {
        try
        {
            var context = PluginService.CreateMenuActionContext(
                capability,
                _vm.SelectedSave?.FullPath,
                message => DisplayAlertAsync(capability.Value.Title, message, "OK"));
            await capability.Value.InvokeAsync(context);
        }
        catch (Exception ex)
        {
            capability.Plugin.Host?.Log.Error("menu action failed", ex);
            await DisplayAlertAsync(capability.Value.Title, $"The action failed: {ex.Message}", "OK");
        }
    }

    private async Task OpenToolAsync(PluginCapability<IEditorTool> capability)
    {
        try
        {
            var context = PluginService.CreateToolContext(capability, _vm.SelectedSave?.FullPath);
            var view = capability.Value.CreateView(context);
            if (view is not View mauiView)
            {
                await DisplayAlertAsync(capability.Value.Title,
                    "This tool returned a view the host could not display.", "OK");
                return;
            }

            await Navigation.PushModalAsync(new PluginToolHostPage(capability.Value.Title, mauiView, context));
        }
        catch (Exception ex)
        {
            capability.Plugin.Host?.Log.Error("editor tool failed to open", ex);
            await DisplayAlertAsync(capability.Value.Title, $"The tool failed to open: {ex.Message}", "OK");
        }
    }
}

/// <summary>A simple modal page that hosts a plugin-provided view with a close button.</summary>
internal sealed class PluginToolHostPage : ModalCleanupPage
{
    private readonly View _toolView;
    private readonly IDisposable? _context;

    public PluginToolHostPage(string title, View toolView, IEditorToolContext context)
    {
        Title = title;
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];
        _toolView = toolView;
        _context = context as IDisposable;

        var close = new Button { Text = "CLOSE", Margin = new Thickness(12) };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        Content = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            Children = { toolView, close },
        };
        Grid.SetRow(toolView, 0);
        Grid.SetRow(close, 1);
    }

    /// <summary>
    /// Disposes the tool context (severing the <c>ActiveSaveChanged</c> subscription a tool
    /// view-model holds) and the view / its view-model when the panel is genuinely closed - not
    /// when the tool pushes a modal over itself (see <see cref="ModalCleanupPage"/>) - so neither
    /// the tool nor the save it parsed outlives the page.
    /// </summary>
    protected override void OnModalRemoved()
    {
        _context?.Dispose();
        (_toolView.BindingContext as IDisposable)?.Dispose();
        (_toolView as IDisposable)?.Dispose();
    }
}
