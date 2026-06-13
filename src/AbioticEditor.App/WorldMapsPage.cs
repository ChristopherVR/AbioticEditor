using System.Collections.ObjectModel;
using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;
using CommunityToolkit.Maui.Storage;
using UeSaveGame;

namespace AbioticEditor.App;

/// <summary>
/// Modal "World Maps" editor. Surfaces the per-actor world-state maps (elevators, buttons,
/// resource nodes, NPC spawners, power sockets, vehicles, portals, triggers, trams, server
/// entitlements) generically through <see cref="IWorldMapFeature"/> - no per-map UI code: pick
/// a world save, pick a feature, pick an entry, and edit its typed fields. SAVE writes the save
/// through the normal writer (keeping a .bak). Built in code so it picks up the live palette,
/// mirroring <see cref="ComparePage"/>.
/// </summary>
public sealed class WorldMapsPage : ContentPage
{
    private readonly MainViewModel _vm;
    private string? _path;
    private WorldSaveData? _data;
    private IReadOnlyList<IWorldMapFeature> _applicable = Array.Empty<IWorldMapFeature>();
    private IWorldMapFeature? _feature;
    private bool _dirty;

    private Label _sourceLabel = null!;
    private Picker _featurePicker = null!;
    private VerticalStackLayout _entriesHost = null!;
    private VerticalStackLayout _detailHost = null!;
    private Button _saveButton = null!;

    public WorldMapsPage(MainViewModel vm)
    {
        _vm = vm;
        Title = "World Maps";
        BackgroundColor = Res("AfPageBackground");
        _path = _vm.SelectedSave?.FullPath ?? _vm.Saves.FirstOrDefault()?.FullPath;
        Content = BuildContent();
        RefreshSourceLabel();
    }

    private static Color Res(string key) => (Color)Application.Current!.Resources[key];

    private View BuildContent()
    {
        _sourceLabel = new Label
        {
            Style = ModalChrome.St("AfMuted"),
            FontSize = 11,
            LineBreakMode = LineBreakMode.MiddleTruncation,
        };

        var quick = new Picker { Title = "Loaded saves…", FontSize = 11, WidthRequest = 320 };
        foreach (var s in _vm.Saves)
        {
            quick.Items.Add(s.DisplayName);
        }
        quick.SelectedIndexChanged += (_, _) =>
        {
            if (quick.SelectedIndex < 0 || quick.SelectedIndex >= _vm.Saves.Count) return;
            _path = _vm.Saves[quick.SelectedIndex].FullPath;
            RefreshSourceLabel();
        };

        var browse = ModalChrome.Button("BROWSE…", primary: false);
        browse.Clicked += async (_, _) => await PickAsync();
        var load = ModalChrome.Button("LOAD", primary: true);
        load.Clicked += async (_, _) => await LoadAsync();

        var sourceCard = ModalChrome.Card("WORLD SAVE",
            "Pick a WorldSave_*.sav (a region save has the most maps). LOAD reads its editable world-state features.",
            new HorizontalStackLayout { Spacing = 10, Children = { quick, browse } },
            _sourceLabel,
            new HorizontalStackLayout { Spacing = 10, Children = { load } });

        _featurePicker = new Picker { Title = "Feature…", FontSize = 12, IsEnabled = false };
        _featurePicker.SelectedIndexChanged += (_, _) => OnFeatureSelected();
        var featureCard = ModalChrome.Card("FEATURE",
            "Each feature is one world-state map. Editing patches only the chosen field; every save keeps a .bak.",
            _featurePicker);

        _entriesHost = new VerticalStackLayout { Spacing = 8 };
        _detailHost = new VerticalStackLayout { Spacing = 10 };

        _saveButton = ModalChrome.Button("SAVE", primary: true);
        _saveButton.IsEnabled = false;
        _saveButton.Clicked += async (_, _) => await SaveAsync();
        var close = ModalChrome.Button("CLOSE", primary: false);
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        return ModalChrome.Scaffold(
            "WORLD STATE", "World Maps",
            new View[] { sourceCard, featureCard, _entriesHost, _detailHost },
            ModalChrome.Footer(_saveButton, close),
            maxWidth: 980);
    }

    private void RefreshSourceLabel()
        => _sourceLabel.Text = _path is null ? "(nothing selected)" : _path;

    private async Task PickAsync()
    {
        try
        {
            var savType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.WinUI, new[] { ".sav" } },
                { DevicePlatform.MacCatalyst, new[] { "sav", "public.data" } },
            });
            var result = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Pick a world .sav file", FileTypes = savType });
            if (result is null) return;
            _path = result.FullPath;
            RefreshSourceLabel();
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && !ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            await ViewUtils.AlertAsync(this, "Picker failed", ex.Message);
        }
    }

    private async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(_path))
        {
            await ViewUtils.AlertAsync(this, "Pick a save", "Choose a world save first.");
            return;
        }
        if (_dirty && !await ViewUtils.ConfirmAsync(this, "Discard changes?",
            "Loading a different save discards your unsaved world-map edits.", "Discard", "Cancel"))
        {
            return;
        }

        try
        {
            var data = await Task.Run(() => WorldSaveReader.ReadFromFile(_path!));
            _data = data;
            _dirty = false;
            _saveButton.IsEnabled = false;
            _applicable = WorldMapFeatures.ApplicableTo(data.Raw);

            _featurePicker.Items.Clear();
            foreach (var f in _applicable)
            {
                _featurePicker.Items.Add($"{f.DisplayName}  ({f.Read(data.Raw).Count})");
            }
            _featurePicker.IsEnabled = _applicable.Count > 0;
            _featurePicker.SelectedIndex = -1;
            _feature = null;
            _entriesHost.Children.Clear();
            _detailHost.Children.Clear();

            if (_applicable.Count == 0)
            {
                _entriesHost.Children.Add(Muted("This save has none of the editable world-state maps."));
            }
        }
        catch (Exception ex)
        {
            await ViewUtils.AlertAsync(this, "Load failed", ex.Message);
        }
    }

    private void OnFeatureSelected()
    {
        _detailHost.Children.Clear();
        _entriesHost.Children.Clear();
        var index = _featurePicker.SelectedIndex;
        if (_data is null || index < 0 || index >= _applicable.Count)
        {
            _feature = null;
            return;
        }
        _feature = _applicable[index];

        var entries = _feature.Read(_data.Raw);
        var rows = new ObservableCollection<EntryRow>(entries.Select(e => new EntryRow(e)));
        var list = new CollectionView
        {
            ItemsSource = rows,
            SelectionMode = SelectionMode.Single,
            HeightRequest = 320,
            ItemTemplate = new DataTemplate(() =>
            {
                var label = new Label { FontSize = 12, FontFamily = "OpenSansSemibold", VerticalOptions = LayoutOptions.Center };
                label.SetBinding(Label.TextProperty, nameof(EntryRow.Label));
                var summary = new Label { FontSize = 11, TextColor = Res("AfTextSecondary"), LineBreakMode = LineBreakMode.TailTruncation };
                summary.SetBinding(Label.TextProperty, nameof(EntryRow.Summary));
                return new VerticalStackLayout { Spacing = 1, Padding = new Thickness(2, 5), Children = { label, summary } };
            }),
        };
        list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is EntryRow row)
            {
                ShowEntryDetail(row.Key);
            }
        };

        _entriesHost.Children.Add(ModalChrome.Card(_feature.DisplayName.ToUpperInvariant(),
            $"{entries.Count} entr{(entries.Count == 1 ? "y" : "ies")}. {_feature.Description} Tap one to edit its fields.",
            list));
    }

    private void ShowEntryDetail(string key)
    {
        _detailHost.Children.Clear();
        if (_feature is null || _data is null) return;

        var entry = _feature.Read(_data.Raw).FirstOrDefault(e => e.Key == key);
        if (entry is null) return;

        var rows = new List<View>();
        foreach (var field in entry.Fields)
        {
            rows.Add(BuildFieldRow(key, field));
        }

        _detailHost.Children.Add(ModalChrome.Card($"EDIT · {entry.Label}", entry.Key, rows.ToArray()));
    }

    private View BuildFieldRow(string entryKey, WorldMapField field)
    {
        var label = new Label
        {
            Text = field.Label + (field.Editable ? string.Empty : "  (read-only)"),
            Style = ModalChrome.St("AfFieldLabel"),
            VerticalOptions = LayoutOptions.Center,
        };

        View control;
        if (!field.Editable)
        {
            control = new Label { Text = field.Value ?? "(none)", Style = ModalChrome.St("AfFieldValue"), VerticalOptions = LayoutOptions.Center };
        }
        else
        {
            switch (field.Kind)
            {
                case WorldFieldKind.Bool:
                    var sw = new Switch
                    {
                        IsToggled = string.Equals(field.Value, "true", StringComparison.OrdinalIgnoreCase),
                        VerticalOptions = LayoutOptions.Center,
                    };
                    sw.Toggled += async (_, e) => await ApplyAsync(entryKey, field.Id, e.Value ? "true" : "false");
                    control = sw;
                    break;
                case WorldFieldKind.Enum when field.Options is { Count: > 0 }:
                    var picker = new Picker { FontSize = 12, WidthRequest = 240 };
                    foreach (var o in field.Options)
                    {
                        picker.Items.Add(o);
                    }
                    picker.SelectedIndex = field.Options.ToList().FindIndex(
                        o => string.Equals(o, field.Value, StringComparison.OrdinalIgnoreCase));
                    picker.SelectedIndexChanged += async (_, _) =>
                    {
                        if (picker.SelectedIndex >= 0)
                        {
                            await ApplyAsync(entryKey, field.Id, field.Options[picker.SelectedIndex]);
                        }
                    };
                    control = picker;
                    break;
                default:
                    var entry = new Entry
                    {
                        Text = field.Value,
                        FontSize = 12,
                        WidthRequest = 220,
                        Keyboard = field.Kind is WorldFieldKind.Integer or WorldFieldKind.Number ? Keyboard.Numeric : Keyboard.Default,
                    };
                    entry.Completed += async (_, _) => await ApplyAsync(entryKey, field.Id, entry.Text);
                    entry.Unfocused += async (_, _) => await ApplyAsync(entryKey, field.Id, entry.Text);
                    control = entry;
                    break;
            }
        }

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Padding = new Thickness(0, 4),
        };
        grid.Add(label, 0, 0);
        grid.Add(control, 1, 0);

        if (field.Hint is not null)
        {
            var hint = new Label { Text = field.Hint, Style = ModalChrome.St("AfMuted"), FontSize = 10 };
            return new VerticalStackLayout { Spacing = 2, Children = { grid, hint } };
        }
        return grid;
    }

    private async Task ApplyAsync(string entryKey, string fieldId, string? value)
    {
        if (_feature is null || _data is null) return;
        var result = _feature.SetField(_data.Raw, entryKey, fieldId, value);
        if (result.IsError)
        {
            await ViewUtils.AlertAsync(this, "Invalid value", result.Error!);
            return;
        }
        if (result.Changed)
        {
            _dirty = true;
            _saveButton.IsEnabled = true;
        }
    }

    private async Task SaveAsync()
    {
        if (_data is null || _path is null || !_dirty) return;
        try
        {
            await Task.Run(() => WorldSaveWriter.WriteToFile(_data, _path));
            _dirty = false;
            _saveButton.IsEnabled = false;
            await ViewUtils.AlertAsync(this, "Saved",
                $"Wrote {Path.GetFileName(_path)} (previous kept as {Path.GetFileName(_path)}.bak).");
        }
        catch (Exception ex)
        {
            await ViewUtils.AlertAsync(this, "Save failed", ex.Message);
        }
    }

    private static Label Muted(string text)
        => new() { Text = text, Style = ModalChrome.St("AfMuted"), FontSize = 12 };

    private sealed class EntryRow
    {
        public EntryRow(WorldMapEntry entry)
        {
            Key = entry.Key;
            Label = entry.Label;
            Summary = string.Join("   ", entry.Fields
                .Where(f => f.Editable)
                .Select(f => $"{f.Id}={f.Value}"));
        }

        public string Key { get; }
        public string Label { get; }
        public string Summary { get; }
    }
}
