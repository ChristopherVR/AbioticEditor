using AbioticEditor.App.Services;
using AbioticEditor.App.ViewModels;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins.Saves;
using AbioticEditor.Plugins.Ui;

namespace AbioticEditor.App.Views;

/// <summary>
/// Builds the plugin-management UI as a set of <see cref="ModalChrome.Card"/> sections:
/// installed plugins (with enable toggles), the save operations applicable to the open save,
/// and the UI / web / menu tools each plugin contributes. Rendered inline in the settings
/// PLUGINS tab and also hosted by the thin <see cref="PluginsPage"/> modal that the plugin
/// host API (<c>abiotic.ui.openPluginsPanel</c>) opens.
///
/// Navigation and alerts route through the supplied <paramref name="owner"/> page, so dialogs
/// (native <c>DisplayAlertAsync</c>) render on top of whatever modal hosts the panel.
/// </summary>
internal sealed class PluginsPanel
{
    private readonly Page _owner;
    private readonly MainViewModel _vm;

    public PluginsPanel(Page owner, MainViewModel vm)
    {
        _owner = owner;
        _vm = vm;
    }

    private static LocalizationResourceManager L => LocalizationResourceManager.Instance;

    /// <summary>One facility card per plugin section, ready to drop into a tab or scroll column.</summary>
    public IReadOnlyList<View> BuildCards()
    {
        var muted = ModalChrome.Col("AfTextSecondary");
        var cards = new List<View>();

        // ---- Installed plugins ----
        var descriptors = PluginService.Descriptors;
        var installedBody = new List<View>();
        if (descriptors.Count == 0)
        {
            installedBody.Add(Hint(L["Plugins_NoneInstalled"], muted));
        }
        else
        {
            foreach (var d in descriptors)
            {
                installedBody.Add(BuildPluginRow(d, muted));
            }
        }
        cards.Add(ModalChrome.Card(L["Plugins_HeaderInstalled"],
            L.Format("Plugins_IntroHint", PluginPaths.UserPluginsDirectory),
            installedBody.ToArray()));

        // ---- Save operations for the open save ----
        var opsBody = new List<View>();
        var openPath = _vm.SelectedSave?.FullPath;
        string? opsHint;
        if (openPath is null)
        {
            opsHint = L["Plugins_NoOpenSaveHint"];
        }
        else
        {
            var openKind = SaveKindDetector.Detect(openPath);
            opsHint = L.Format("Plugins_OpenSaveHint", Path.GetFileName(openPath), openKind);
            var applicable = PluginService.SaveOperations
                .Where(c => SaveKindDetector.Matches(c.Value.AppliesTo, openKind))
                .ToList();
            if (applicable.Count == 0)
            {
                opsBody.Add(Hint(L["Plugins_NoOperationApplies"], muted));
            }
            else
            {
                foreach (var op in applicable)
                {
                    opsBody.Add(BuildOperationCard(op, openPath, muted));
                }
            }
        }
        cards.Add(ModalChrome.Card(L["Plugins_HeaderSaveOperations"], opsHint, opsBody.ToArray()));

        // ---- UI tools ----
        cards.Add(SectionCard(L["Plugins_HeaderTools"], PluginService.EditorTools,
            L["Plugins_NoUiTools"], BuildToolRow, muted));

        // ---- Web (HTML/React) tools ----
        cards.Add(SectionCard(L["Plugins_HeaderWebTools"], PluginService.WebTools,
            L["Plugins_NoWebTools"], BuildWebToolRow, muted));

        // ---- Menu actions ----
        cards.Add(SectionCard(L["Plugins_HeaderMenuActions"], PluginService.MenuActions,
            L["Plugins_NoMenuActions"], BuildMenuActionRow, muted));

        return cards;
    }

    private static View SectionCard<T>(string header, IReadOnlyList<T> items, string emptyHint,
        Func<T, View> buildRow, Color muted)
    {
        var body = new List<View>();
        if (items.Count == 0)
        {
            body.Add(Hint(emptyHint, muted));
        }
        else
        {
            foreach (var item in items)
            {
                body.Add(buildRow(item));
            }
        }
        return ModalChrome.Card(header, null, body.ToArray());
    }

    private static Label Hint(string text, Color muted)
        => new() { Text = text, FontSize = 11, TextColor = muted };

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
                await _owner.DisplayAlertAsync(L["Plugins_Title"], L["Plugins_ManifestUpdateFailed"], L["Common_Ok"]);
                enableSwitch.IsToggled = d.Manifest.Enabled;
                return;
            }
            await _owner.DisplayAlertAsync(L["Plugins_Title"],
                L.Format("Plugins_EnabledToast", d.Manifest.Name, L[e.Value ? "Plugins_Enabled" : "Plugins_Disabled"]),
                L["Common_Ok"]);
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

        return new Border { Padding = new Thickness(12, 8), Margin = new Thickness(0, 2), Content = stack };
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

        var run = new Button { Text = L["Plugins_Run"] };
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
        var confirm = await _owner.DisplayAlertAsync(op.DisplayName,
            L.Format("Plugins_RunConfirmMessage", op.DisplayName, Path.GetFileName(savePath)),
            L["Plugins_RunConfirmButton"], L["Common_Cancel"]);
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
                await _owner.DisplayAlertAsync(op.DisplayName,
                    L.Format("Plugins_OperationFailed", outcome.Result.Message), L["Common_Ok"]);
                return;
            }

            await _owner.DisplayAlertAsync(op.DisplayName,
                L.Format("Plugins_OperationResult", outcome.Result.Message,
                    L[outcome.Wrote ? "Plugins_SaveWritten" : "Plugins_NoChangeNeeded"]),
                L["Common_Ok"]);

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
        row.Add(new Label { Text = $"{glyph}{tool.Title}", VerticalOptions = LayoutOptions.Center }, 0, 0);

        var open = new Button { Text = L["Plugins_Open"] };
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

        var open = new Button { Text = L["Plugins_Open"] };
        open.Clicked += async (_, _) => await OpenWebToolAsync(capability);
        row.Add(open, 1, 0);
        return row;
    }

    private async Task OpenWebToolAsync(PluginCapability<IWebTool> capability)
    {
        try
        {
            var context = PluginService.CreateWebToolContext(capability, () => _vm.SelectedSave?.FullPath);
            await _owner.Navigation.PushModalAsync(new WebToolHostPage(capability, context));
        }
        catch (Exception ex)
        {
            capability.Plugin.Host?.Log.Error("web tool failed to open", ex);
            await _owner.DisplayAlertAsync(capability.Value.Title,
                L.Format("Plugins_WebToolFailed", ex.Message), L["Common_Ok"]);
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

        var run = new Button { Text = L["Plugins_Run"] };
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
                message => _owner.DisplayAlertAsync(capability.Value.Title, message, "OK"));
            await capability.Value.InvokeAsync(context);
        }
        catch (Exception ex)
        {
            capability.Plugin.Host?.Log.Error("menu action failed", ex);
            await _owner.DisplayAlertAsync(capability.Value.Title,
                L.Format("Plugins_ActionFailed", ex.Message), L["Common_Ok"]);
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
                await _owner.DisplayAlertAsync(capability.Value.Title,
                    L["Plugins_ToolViewUnsupported"], L["Common_Ok"]);
                return;
            }

            await _owner.Navigation.PushModalAsync(new PluginToolHostPage(capability.Value.Title, mauiView, context));
        }
        catch (Exception ex)
        {
            capability.Plugin.Host?.Log.Error("editor tool failed to open", ex);
            await _owner.DisplayAlertAsync(capability.Value.Title,
                L.Format("Plugins_ToolFailed", ex.Message), L["Common_Ok"]);
        }
    }
}
