using System.Collections.ObjectModel;

using AbioticEditor.App.ViewModels;
using AbioticEditor.App.Views;
using AbioticEditor.Core.Compare;
using AbioticEditor.Core.Saves;

using CommunityToolkit.Maui.Storage;

namespace AbioticEditor.App;

/// <summary>
/// Modal save-comparison sheet. Diffs two save files, or two folders of saves (e.g. a world
/// vs one of its backups), and lists every property-level difference. Built in code so it
/// picks up the live palette - mirrors <see cref="SettingsPage"/>.
/// </summary>
public sealed class ComparePage : ContentPage
{
    private enum CompareMode { Files, Folders }

    private readonly MainViewModel _vm;
    // Default to folder-vs-folder: an Abiotic "save" is a folder (a world, a backup, or a wgs
    // container), so picking a folder matches how the rest of the editor opens saves. File-vs-file
    // is still available via the toggle for diffing two individual .sav files.
    private CompareMode _mode = CompareMode.Folders;
    private string? _pathA;
    private string? _pathB;

    private Label _slotALabel = null!;
    private Label _slotBLabel = null!;
    private Button _compareButton = null!;
    private ActivityIndicator _busy = null!;
    private VerticalStackLayout _resultsHost = null!;
    private Label _summaryLabel = null!;

    public ComparePage(MainViewModel vm)
    {
        _vm = vm;
        Title = Services.LocalizationResourceManager.Instance["Compare_Title"];
        BackgroundColor = Res("AfPageBackground");

        // Default the first slot to whatever the editor currently has open / loaded: the open
        // folder in folder mode, the selected save in file mode.
        _pathA = _mode == CompareMode.Folders
            ? _vm.FolderPath
            : _vm.SelectedSave?.FullPath ?? _vm.Saves.FirstOrDefault()?.FullPath;

        Content = BuildContent();
        RefreshSlotLabels();
    }

    private static Color Res(string key) => (Color)Application.Current!.Resources[key];

    private View BuildContent()
    {
        var accent = Res("AfAccentOrange");
        var muted = Res("AfTextSecondary");

        var modeToggle = ModalChrome.Segmented(
            new[]
            {
                Services.LocalizationResourceManager.Instance["Compare_ModeFileVsFile"],
                Services.LocalizationResourceManager.Instance["Compare_ModeFolderVsFolder"],
            },
            selected: _mode == CompareMode.Files ? 0 : 1,
            onChange: i => SetMode(i == 0 ? CompareMode.Files : CompareMode.Folders));

        _slotALabel = new Label { TextColor = muted, FontSize = 12, LineBreakMode = LineBreakMode.MiddleTruncation };
        _slotBLabel = new Label { TextColor = muted, FontSize = 12, LineBreakMode = LineBreakMode.MiddleTruncation };

        _compareButton = new Button { Text = Services.LocalizationResourceManager.Instance["Compare_CompareButton"] };
        _compareButton.Clicked += async (_, _) => await RunCompareAsync();

        _busy = new ActivityIndicator { IsRunning = false, IsVisible = false, Color = accent, HeightRequest = 22, VerticalOptions = LayoutOptions.Center };

        _summaryLabel = new Label { FontSize = 13, FontFamily = "OpenSansSemibold", IsVisible = false };
        // Holds the rendered result cards (semantic sections + raw deep-dive); appended to
        // the scaffold body directly so each result is its own facility card.
        _resultsHost = new VerticalStackLayout { Spacing = 14 };

        var modeCard = ModalChrome.Card(Services.LocalizationResourceManager.Instance["Compare_ModeCardHeader"],
            Services.LocalizationResourceManager.Instance["Compare_ModeCardDescription"],
            modeToggle);

        // A and B sit side by side: a "compare" reads naturally left-to-right, and the
        // two columns fill the sheet width instead of stacking in a thin strip.
        var sourcesGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 18,
        };
        sourcesGrid.Add(BuildSourceColumn(isA: true), 0, 0);
        sourcesGrid.Add(BuildSourceColumn(isA: false), 1, 0);

        var sourcesCard = ModalChrome.Card(Services.LocalizationResourceManager.Instance["Compare_SourcesCardHeader"], null,
            sourcesGrid);

        var runCard = ModalChrome.Card(Services.LocalizationResourceManager.Instance["Compare_CompareCardHeader"], null,
            new HorizontalStackLayout { Spacing = 12, Children = { _compareButton, _busy } },
            _summaryLabel);

        var close = ModalChrome.Button(Services.LocalizationResourceManager.Instance["Common_Close"], primary: false);
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        return ModalChrome.Scaffold(
            Services.LocalizationResourceManager.Instance["Compare_ScaffoldEyebrow"],
            Services.LocalizationResourceManager.Instance["Compare_ScaffoldTitle"],
            new View[] { modeCard, sourcesCard, runCard, _resultsHost },
            ModalChrome.Footer(close),
            maxWidth: 980);
    }

    /// <summary>
    /// One source column (A or B): an eyebrow label, an optional quick-pick of the editor's
    /// loaded saves (file mode only), a BROWSE button, and the selected-path label. The two
    /// columns sit side by side so the sheet reads as a left/right comparison.
    /// </summary>
    private View BuildSourceColumn(bool isA)
    {
        var loc = Services.LocalizationResourceManager.Instance;

        var browse = ModalChrome.Button(loc["Compare_BrowseButton"], primary: false);
        browse.HorizontalOptions = LayoutOptions.Fill;
        browse.Clicked += async (_, _) => await PickAsync(isA);

        var slotLabel = isA ? _slotALabel : _slotBLabel;

        var column = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                ModalChrome.SubLabel(loc[isA ? "Compare_SourceALabel" : "Compare_SourceBLabel"]),
            },
        };

        // Quick-pick from the saves already loaded in the editor (file mode only).
        if (_mode == CompareMode.Files)
        {
            var picker = new Picker
            {
                Title = loc["Compare_QuickPickerTitle"],
                FontSize = 11,
                HorizontalOptions = LayoutOptions.Fill,
            };
            foreach (var s in _vm.Saves)
            {
                picker.Items.Add(s.DisplayName);
            }
            picker.SelectedIndexChanged += (_, _) =>
            {
                if (picker.SelectedIndex < 0 || picker.SelectedIndex >= _vm.Saves.Count) return;
                var path = _vm.Saves[picker.SelectedIndex].FullPath;
                if (isA) _pathA = path; else _pathB = path;
                RefreshSlotLabels();
            };
            column.Children.Add(picker);
        }

        column.Children.Add(browse);
        column.Children.Add(slotLabel);
        return column;
    }

    private void SetMode(CompareMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _pathA = _pathB = null;
        ClearResults();
        // Rebuild so the quick-pickers show/hide for the new mode.
        Content = BuildContent();
        RefreshSlotLabels();
    }

    private void RefreshSlotLabels()
    {
        _slotALabel.Text = _pathA is null ? Services.LocalizationResourceManager.Instance["Compare_NothingSelected"] : _pathA;
        _slotBLabel.Text = _pathB is null ? Services.LocalizationResourceManager.Instance["Compare_NothingSelected"] : _pathB;
    }

    private async Task PickAsync(bool isA)
    {
        try
        {
            string? picked;
            if (_mode == CompareMode.Folders)
            {
                var result = await FolderPicker.PickAsync(CancellationToken.None);
                if (!result.IsSuccessful || result.Folder is null) return;
                picked = result.Folder.Path;
            }
            else
            {
                var savType = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".sav" } },
                    { DevicePlatform.MacCatalyst, new[] { "sav", "public.data" } },
                });
                var result = await FilePicker.PickAsync(new PickOptions { PickerTitle = Services.LocalizationResourceManager.Instance["Compare_PickFileTitle"], FileTypes = savType });
                if (result is null) return;
                picked = result.FullPath;
            }

            if (isA) _pathA = picked; else _pathB = picked;
            RefreshSlotLabels();
        }
        catch (Exception ex) when (!IsCancellation(ex))
        {
            await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_PickerFailedTitle"], ex.Message);
        }
    }

    private static bool IsCancellation(Exception ex) =>
        ex is OperationCanceledException || ex.Message.Contains("cancel", StringComparison.OrdinalIgnoreCase);

    private async Task RunCompareAsync()
    {
        if (string.IsNullOrEmpty(_pathA) || string.IsNullOrEmpty(_pathB))
        {
            await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_PickTwoTitle"], Services.LocalizationResourceManager.Instance["Compare_PickTwoMessage"]);
            return;
        }

        ClearResults();
        _busy.IsVisible = _busy.IsRunning = true;
        _compareButton.IsEnabled = false;

        try
        {
            if (_mode == CompareMode.Folders)
            {
                var folderDiff = await Task.Run(() => SaveFolderComparer.Compare(_pathA!, _pathB!));
                ShowFolderResult(folderDiff);
            }
            else
            {
                // Make sure the catalogs are ready so the semantic view resolves names + icons.
                await Services.GameDataServices.EnsureLoadedAsync();
                var diff = await Task.Run(() => SaveComparer.CompareFiles(_pathA!, _pathB!));
                var semantic = await Task.Run(() => TryBuildSemantic(_pathA!, _pathB!));
                ShowFileResult(diff, semantic);
            }
        }
        catch (Exception ex)
        {
            await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_ComparisonFailedTitle"], ex.Message);
        }
        finally
        {
            _busy.IsVisible = _busy.IsRunning = false;
            _compareButton.IsEnabled = true;
        }
    }

    private void ClearResults()
    {
        _resultsHost.Children.Clear();
        _summaryLabel.IsVisible = false;
    }

    /// <summary>
    /// Builds a domain-aware semantic diff when both files are the same kind: two player saves
    /// → player sections, two world/metadata saves → world sections. Null when they aren't a
    /// matched, supported pair (the raw property diff covers those).
    ///
    /// The save kind is classified from each file's header BEFORE parsing, so a world save is
    /// never routed through <see cref="Core.PlayerSaves.PlayerSaveReader"/> (which would log a
    /// scary parse Error and rethrow). Any unexpected failure is caught, logged as a Warn, and
    /// turned into a null result so a single odd file never crashes the comparison - the raw
    /// property diff still renders.
    /// </summary>
    private static (string Kind, List<SemanticSection> Sections)? TryBuildSemantic(string a, string b)
    {
        try
        {
            var kindA = ClassifyKind(a);
            var kindB = ClassifyKind(b);

            // Only a matched, supported pair gets a semantic view. Different kinds (or an
            // unknown/customization kind) fall through to the raw diff only.
            if (kindA != kindB) return null;

            if (kindA == Core.Compatibility.SaveKind.Character)
            {
                var pa = Core.PlayerSaves.PlayerSaveReader.ReadFromFile(a);
                var pb = Core.PlayerSaves.PlayerSaveReader.ReadFromFile(b);
                return ("PLAYER", PlayerSemanticDiff.Build(pa, pb));
            }

            if (kindA is Core.Compatibility.SaveKind.World or Core.Compatibility.SaveKind.Metadata)
            {
                var wa = Core.WorldSaves.WorldSaveReader.ReadFromFile(a);
                var wb = Core.WorldSaves.WorldSaveReader.ReadFromFile(b);
                return ("WORLD", WorldSemanticDiff.Build(wa, wb));
            }

            return null;
        }
        catch (Exception ex)
        {
            // Never let a single unreadable / unexpected file throw out of the comparison; the
            // raw property diff still covers it.
            Core.Diagnostics.EditorLog.Warn("Compare", $"Semantic diff unavailable for '{a}' / '{b}'", ex);
            return null;
        }
    }

    /// <summary>Classifies a save's kind from its header alone (cheap, never parses the body).</summary>
    private static Core.Compatibility.SaveKind ClassifyKind(string path)
    {
        try
        {
            var (saveClass, _) = Core.Saves.SaveFolderScanner.ReadHeaderInfo(path);
            return Core.Compatibility.SaveVersionRegistry.KindOfClassPath(saveClass);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.EditorLog.Warn("Compare", $"Could not read header for '{path}'", ex);
            return Core.Compatibility.SaveKind.Unknown;
        }
    }

    private void ShowFileResult(SaveDiff diff, (string Kind, List<SemanticSection> Sections)? semantic)
    {
        _summaryLabel.IsVisible = true;
        _summaryLabel.TextColor = diff.AreIdentical || diff.AreMeaningfullyIdentical
            ? Res("AfTerminalGreen")
            : Res("AfTextPrimary");
        _summaryLabel.Text = diff.AreIdentical
            ? Services.LocalizationResourceManager.Instance["Compare_SummaryIdentical"]
            : diff.AreMeaningfullyIdentical
                ? Services.LocalizationResourceManager.Instance.Format("Compare_SummaryNoGameplayDiff", diff.NoiseCount)
                : $"{diff.MeaningfulSummary}.";

        if (diff.LeftSaveClass != diff.RightSaveClass)
        {
            _resultsHost.Children.Add(Warn(Services.LocalizationResourceManager.Instance.Format("Compare_SaveClassesDiffer", diff.LeftSaveClass, diff.RightSaveClass)));
        }
        if (diff.Truncated)
        {
            _resultsHost.Children.Add(Warn(Services.LocalizationResourceManager.Instance["Compare_SavesTooLarge"]));
        }

        // Domain-aware, summary-first view when both saves are the same supported kind; the
        // raw property diff stays available below as a deep-dive.
        if (semantic is { } sem)
        {
            if (sem.Sections.Count == 0)
            {
                _resultsHost.Children.Add(ModalChrome.Card(Services.LocalizationResourceManager.Instance.Format("Compare_KindSummaryHeader", sem.Kind),
                    Services.LocalizationResourceManager.Instance["Compare_NoMeaningfulDiff"]));
            }
            else
            {
                _resultsHost.Children.Add(BuildOverviewCard(sem.Sections));
                foreach (var section in sem.Sections)
                {
                    _resultsHost.Children.Add(PlayerSemanticDiff.RenderSection(section));
                }
            }
        }

        if (!diff.AreIdentical)
        {
            _resultsHost.Children.Add(BuildRawCard(diff, startExpanded: semantic is null));
        }

        _resultsHost.Children.Add(BuildExportCard(() => Core.Compare.SaveDiffReport.ForFile(diff, BuildSemanticMarkdown(semantic)),
            $"compare_{Path.GetFileNameWithoutExtension(diff.LeftLabel)}_vs_{Path.GetFileNameWithoutExtension(diff.RightLabel)}.md"));
    }

    /// <summary>Renders the (App-side) semantic sections as a Markdown block for the export report.</summary>
    private static string? BuildSemanticMarkdown((string Kind, List<SemanticSection> Sections)? semantic)
    {
        if (semantic is not { } sem || sem.Sections.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.Append("## ").Append(CapitalizeWord(sem.Kind)).Append(" summary\n\n");
        foreach (var section in sem.Sections)
        {
            sb.Append("### ").Append(section.Title).Append("\n\n");
            sb.Append('_').Append(section.Summary).Append("_\n\n");
            foreach (var s in section.Scalars)
            {
                sb.Append("- ").Append(s.Label).Append(": ").Append(s.A).Append(" -> ").Append(s.B).Append('\n');
            }
            foreach (var item in section.OnlyA)
            {
                sb.Append("- only in A: ").Append(item.DisplayName).Append('\n');
            }
            foreach (var item in section.OnlyB)
            {
                sb.Append("- only in B: ").Append(item.DisplayName).Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string CapitalizeWord(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

    /// <summary>
    /// A card with COPY (to clipboard) and SAVE (to a .md file) buttons that export the
    /// comparison as Markdown. <paramref name="build"/> is deferred so the (potentially large)
    /// report string is only built on demand.
    /// </summary>
    private View BuildExportCard(Func<string> build, string suggestedFileName)
    {
        var copy = ModalChrome.Button(Services.LocalizationResourceManager.Instance["Compare_CopyMarkdownButton"], primary: false);
        copy.Clicked += async (_, _) =>
        {
            try
            {
                await Clipboard.SetTextAsync(build());
                await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_CopiedTitle"], Services.LocalizationResourceManager.Instance["Compare_CopiedMessage"]);
            }
            catch (Exception ex)
            {
                await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_ExportFailedTitle"], ex.Message);
            }
        };

        var save = ModalChrome.Button(Services.LocalizationResourceManager.Instance["Compare_SaveMdButton"], primary: false);
        save.Clicked += async (_, _) =>
        {
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(build());
                using var stream = new MemoryStream(bytes);
                var result = await FileSaver.SaveAsync(suggestedFileName, stream, CancellationToken.None);
                if (result.IsSuccessful)
                {
                    await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_SavedTitle"], Services.LocalizationResourceManager.Instance.Format("Compare_SavedMessage", result.FilePath));
                }
            }
            catch (Exception ex) when (!IsCancellation(ex))
            {
                await this.AlertAsync(Services.LocalizationResourceManager.Instance["Compare_ExportFailedTitle"], ex.Message);
            }
        };

        return ModalChrome.Card(Services.LocalizationResourceManager.Instance["Compare_ExportCardHeader"],
            Services.LocalizationResourceManager.Instance["Compare_ExportCardDescription"],
            new HorizontalStackLayout { Spacing = 12, Children = { copy, save } });
    }

    /// <summary>A leading "what changed" card: one line per differing category.</summary>
    private static View BuildOverviewCard(IReadOnlyList<SemanticSection> sections)
    {
        var rows = new List<View>();
        foreach (var s in sections)
        {
            rows.Add(new Label
            {
                Text = $"• {s.Title}: {s.Summary}",
                Style = ModalChrome.St("AfFieldValue"),
                FontSize = 12,
            });
        }
        return ModalChrome.Card(Services.LocalizationResourceManager.Instance["Compare_WhatsDifferentHeader"], Services.LocalizationResourceManager.Instance["Compare_WhatsDifferentDescription"], rows.ToArray());
    }

    /// <summary>The raw property diff, collapsed by default behind a toggle, with a noise switch.</summary>
    private static View BuildRawCard(SaveDiff diff, bool startExpanded)
    {
        var listHost = new VerticalStackLayout();
        void Rebuild(bool includeNoise) { listHost.Children.Clear(); listHost.Children.Add(BuildDiffList(diff, includeNoise)); }

        var content = new VerticalStackLayout { Spacing = 10, IsVisible = startExpanded };
        if (diff.NoiseCount > 0)
        {
            var noiseSwitch = new Switch { IsToggled = false, VerticalOptions = LayoutOptions.Center };
            noiseSwitch.Toggled += (_, e) => Rebuild(e.Value);
            content.Children.Add(new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    noiseSwitch,
                    new Label
                    {
                        Text = Services.LocalizationResourceManager.Instance.Format("Compare_ShowNoiseToggle", diff.NoiseCount),
                        FontSize = 11,
                        TextColor = Res("AfTextSecondary"),
                        VerticalOptions = LayoutOptions.Center,
                    },
                },
            });
        }
        Rebuild(false);
        content.Children.Add(listHost);

        var toggle = ModalChrome.Button(startExpanded ? Services.LocalizationResourceManager.Instance["Compare_HideRawDifferences"] : Services.LocalizationResourceManager.Instance["Compare_ShowRawDifferences"], primary: false);
        toggle.HorizontalOptions = LayoutOptions.Start;
        toggle.Clicked += (_, _) =>
        {
            content.IsVisible = !content.IsVisible;
            toggle.Text = content.IsVisible ? Services.LocalizationResourceManager.Instance["Compare_HideRawDifferences"] : Services.LocalizationResourceManager.Instance["Compare_ShowRawDifferences"];
        };

        return ModalChrome.Card(Services.LocalizationResourceManager.Instance["Compare_RawPropertyDiffHeader"],
            Services.LocalizationResourceManager.Instance.Format("Compare_RawPropertyDiffDescription", diff.MeaningfulCount, diff.NoiseCount),
            toggle, content);
    }

    private void ShowFolderResult(FolderDiff folderDiff)
    {
        _summaryLabel.IsVisible = true;
        _summaryLabel.TextColor = folderDiff.AreIdentical ? Res("AfTerminalGreen") : Res("AfTextPrimary");
        _summaryLabel.Text = folderDiff.AreIdentical
            ? Services.LocalizationResourceManager.Instance["Compare_FoldersIdentical"]
            : Services.LocalizationResourceManager.Instance.Format("Compare_FolderSummary", folderDiff.DifferingCount, folderDiff.IdenticalCount, folderDiff.OnlyLeftCount, folderDiff.OnlyRightCount)
              + (folderDiff.ErrorCount > 0 ? Services.LocalizationResourceManager.Instance.Format("Compare_FolderSummaryErrors", folderDiff.ErrorCount) : string.Empty);

        var rows = new ObservableCollection<FolderRow>(folderDiff.Files.Select(f => new FolderRow(f)));
        var list = new CollectionView
        {
            ItemsSource = rows,
            SelectionMode = SelectionMode.Single,
            HeightRequest = 460,
            ItemTemplate = new DataTemplate(() =>
            {
                var glyph = new Label { FontFamily = "OpenSansSemibold", FontSize = 12, WidthRequest = 70, VerticalOptions = LayoutOptions.Center };
                glyph.SetBinding(Label.TextProperty, nameof(FolderRow.Tag));
                glyph.SetBinding(Label.TextColorProperty, nameof(FolderRow.Color));

                var name = new Label { FontSize = 12, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.MiddleTruncation };
                name.SetBinding(Label.TextProperty, nameof(FolderRow.Text));

                var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, Padding = new Thickness(2, 4) };
                grid.Add(glyph, 0, 0);
                grid.Add(name, 1, 0);
                return grid;
            }),
        };
        list.SelectionChanged += async (_, e) =>
        {
            if ((e.CurrentSelection.Count > 0 ? e.CurrentSelection[0] : null) is FolderRow row)
            {
                ((CollectionView)list).SelectedItem = null;
                if (row.Entry.Status == FolderEntryStatus.Differs && row.Entry.Diff is not null)
                {
                    await Navigation.PushModalAsync(new DiffDetailPage(row.Entry.RelativePath, row.Entry.Diff));
                }
            }
        };

        _resultsHost.Children.Add(new Label
        {
            Text = Services.LocalizationResourceManager.Instance["Compare_TapDifferingFile"],
            FontSize = 11,
            TextColor = Res("AfTextSecondary"),
            Margin = new Thickness(0, 6, 0, 0),
        });
        _resultsHost.Children.Add(list);

        _resultsHost.Children.Add(BuildExportCard(
            () => Core.Compare.SaveDiffReport.ForFolder(folderDiff),
            "folder_compare.md"));
    }

    /// <summary>Shared difference list, virtualized so big saves stay responsive.</summary>
    internal static View BuildDiffList(SaveDiff diff, bool includeNoise = false)
    {
        var source = includeNoise ? diff.Differences : diff.Differences.Where(d => !d.IsNoise);
        var rows = new ObservableCollection<DiffRow>(source.Select(d => new DiffRow(d)));
        return new CollectionView
        {
            ItemsSource = rows,
            HeightRequest = 460,
            ItemTemplate = new DataTemplate(() =>
            {
                var glyph = new Label { FontFamily = "OpenSansSemibold", FontSize = 13, WidthRequest = 16, VerticalOptions = LayoutOptions.Start };
                glyph.SetBinding(Label.TextProperty, nameof(DiffRow.Glyph));
                glyph.SetBinding(Label.TextColorProperty, nameof(DiffRow.Color));

                var path = new Label { FontSize = 12, FontFamily = "OpenSansSemibold" };
                path.SetBinding(Label.TextProperty, nameof(DiffRow.Path));

                var detail = new Label { FontSize = 11, TextColor = Res("AfTextSecondary") };
                detail.SetBinding(Label.TextProperty, nameof(DiffRow.Detail));

                var text = new VerticalStackLayout { Spacing = 1, Children = { path, detail } };

                var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8, Padding = new Thickness(2, 5) };
                grid.Add(glyph, 0, 0);
                grid.Add(text, 1, 0);
                return grid;
            }),
        };
    }

    private static Label Warn(string text) => new()
    {
        Text = "⚠ " + text,
        FontSize = 11,
        TextColor = Res("AfHazardYellow"),
        Margin = new Thickness(0, 4, 0, 0),
    };

    // ===== Row view models =====

    private sealed class DiffRow
    {
        public DiffRow(SaveLeafDiff d)
        {
            // Tag non-gameplay rows so it's clear why they're folded away by default.
            var suffix = d.IsNoise ? $"   [{d.Category.ToString().ToLowerInvariant()}]" : string.Empty;
            Path = d.Path;
            (Glyph, Color, Detail) = d.Kind switch
            {
                SaveDiffKind.Added => ("+", Res("AfTerminalGreen"), (d.Right ?? string.Empty) + suffix),
                SaveDiffKind.Removed => ("−", Res("AfAlertRed"), (d.Left ?? string.Empty) + suffix),
                _ => ("~", Res("AfHazardYellow"), $"{d.Left}  →  {d.Right}{suffix}"),
            };
        }

        public string Glyph { get; }
        public Color Color { get; }
        public string Path { get; }
        public string Detail { get; }
    }

    private sealed class FolderRow
    {
        public FolderRow(FolderFileComparison entry)
        {
            Entry = entry;
            (Tag, Color) = entry.Status switch
            {
                FolderEntryStatus.Identical => ("==", Res("AfTextMuted")),
                FolderEntryStatus.Differs => ("~~", Res("AfHazardYellow")),
                FolderEntryStatus.OnlyLeft => ("A only", Res("AfAlertRed")),
                FolderEntryStatus.OnlyRight => ("B only", Res("AfTerminalGreen")),
                _ => ("error", Res("AfAlertRed")),
            };
            Text = entry.Status switch
            {
                FolderEntryStatus.Differs when entry.Diff is not null =>
                    $"{entry.RelativePath}  ({entry.Diff.MeaningfulCount} gameplay, {entry.DifferenceCount} total)",
                FolderEntryStatus.Differs => $"{entry.RelativePath}  ({entry.DifferenceCount} difference(s))",
                FolderEntryStatus.Error => $"{entry.RelativePath}  ({entry.Error})",
                _ => entry.RelativePath,
            };
        }

        public FolderFileComparison Entry { get; }
        public string Tag { get; }
        public Color Color { get; }
        public string Text { get; }
    }
}

/// <summary>Modal showing one file's property differences (folder-comparison drill-down).</summary>
internal sealed class DiffDetailPage : ContentPage
{
    public DiffDetailPage(string title, SaveDiff diff)
    {
        Title = title;
        BackgroundColor = (Color)Application.Current!.Resources["AfPageBackground"];

        var close = ModalChrome.Button(Services.LocalizationResourceManager.Instance["Common_Close"], primary: false);
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();

        var card = ModalChrome.Card(title.ToUpperInvariant(),
            $"{diff.MeaningfulSummary}.",
            ComparePage.BuildDiffList(diff));

        Content = ModalChrome.Scaffold(
            Services.LocalizationResourceManager.Instance["Compare_FileDifferencesEyebrow"], title,
            new View[] { card },
            ModalChrome.Footer(close),
            maxWidth: 980);
    }
}
