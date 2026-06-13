using System.Globalization;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Ui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace AbioticEditor.Samples.PlaytimeDashboard;

/// <summary>
/// Entry point: registers one UI tool. The host (GUI only) lists it in the Plugins panel
/// and calls <see cref="DashboardTool.CreateView"/> when the user opens it. The CLI loads
/// this assembly's metadata too but never calls CreateView, so the MAUI dependency is only
/// ever exercised inside a GUI process.
/// </summary>
public sealed class PlaytimeDashboardPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
        => registry.AddEditorTool(new DashboardTool());
}

/// <summary>
/// A read-only dashboard that shows headline numbers for whatever save the editor currently
/// has open, refreshing when the user switches files. Demonstrates dynamic UI component
/// loading: the host hands the plugin a context and hosts whatever <see cref="View"/> the
/// plugin returns, with no compile-time knowledge of this type.
/// </summary>
public sealed class DashboardTool : IEditorTool
{
    public string Id => "playtime-dashboard";

    public string Title => "Dashboard";

    public string Glyph => "📊";

    public EditorToolPlacement Placement => EditorToolPlacement.Panel;

    public object CreateView(IEditorToolContext context)
    {
        var title = new Label
        {
            Text = "SAVE DASHBOARD",
            FontAttributes = FontAttributes.Bold,
            FontSize = 18,
        };
        var body = new Label { LineBreakMode = LineBreakMode.WordWrap };

        void Refresh()
        {
            body.Text = Describe(context);
        }

        // Live-update when the open save changes. The host disposes the context when the panel
        // closes, which nulls this event - so the closure (and the view it captures) is released
        // and can't outlive the panel. (A view-model-based tool would unsubscribe in Dispose;
        // see SaveInspector.)
        context.ActiveSaveChanged += (_, _) => Refresh();
        Refresh();

        return new ScrollView
        {
            Padding = new Thickness(20),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    title,
                    new Label
                    {
                        Text = $"Provided by a plugin · host '{context.Host.HostKind}' · SDK {context.Host.SdkVersion}",
                        FontSize = 11,
                        TextColor = Colors.Gray,
                    },
                    body,
                },
            },
        };
    }

    private static string Describe(IEditorToolContext context)
    {
        if (context.ActiveSave is null)
        {
            return "No save is open. Load a save in the editor and this dashboard will fill in.";
        }

        var kind = context.ActiveSaveKind ?? SaveKind.Any;
        var fileName = context.ActiveSavePath is { } p ? Path.GetFileName(p) : "(in memory)";
        var propertyCount = context.ActiveSave.Properties?.Count ?? 0;

        var lines = new List<string>
        {
            $"File: {fileName}",
            $"Kind: {kind}",
            $"Save class: {context.ActiveSave.SaveClass?.Value ?? "?"}",
            $"Top-level properties: {propertyCount}",
        };

        if (kind == SaveKind.Player)
        {
            try
            {
                var data = PlayerSaveReader.ReadFrom(context.ActiveSave);
                var topLevel = data.Skills.Count == 0 ? 0 : data.Skills.Max(s => s.Level);
                lines.Add(string.Empty);
                lines.Add($"Money: {data.Stats.Money.ToString(CultureInfo.InvariantCulture)}");
                lines.Add($"Skills: {data.Skills.Count} (top level {topLevel})");
                lines.Add($"Recipes unlocked: {data.Recipes.Count}");
                lines.Add($"Traits: {data.Traits.Count}");
            }
            catch (Exception ex)
            {
                context.Host.Log.Warn($"dashboard could not read player details: {ex.Message}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
