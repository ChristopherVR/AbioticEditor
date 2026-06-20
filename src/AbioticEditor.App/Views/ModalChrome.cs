using Microsoft.Maui.Controls.Shapes;

namespace AbioticEditor.App.Views;

/// <summary>
/// Shared "facility" chrome for the code-built modal sheets (Settings, Compare, plugin
/// drill-downs) so they read like the in-game / main-window UI instead of a bare form:
/// a branded header bar + hazard stripe, paneled section cards, and a footer action bar.
/// Built in code (not XAML) because the sheets are code-built - they re-pick the live
/// palette every time they open / a theme switches.
/// </summary>
internal static class ModalChrome
{
    public static Style St(string key) => (Style)Application.Current!.Resources[key];
    public static Color Col(string key) => (Color)Application.Current!.Resources[key];

    /// <summary>
    /// Full modal scaffold: branded header + hazard stripe + a scrollable, centred column
    /// of section <paramref name="cards"/>, and a sticky <paramref name="footer"/> bar. An
    /// optional <paramref name="pinnedHeader"/> (e.g. a tab strip) sits between the hazard
    /// stripe and the scroll area so it stays put while the cards scroll under it.
    /// </summary>
    public static Grid Scaffold(string eyebrow, string title, IEnumerable<View> cards, View footer, double maxWidth = 760, View? pinnedHeader = null)
    {
        var column = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(24, 22, 24, 28),
            MaximumWidthRequest = maxWidth,
            HorizontalOptions = LayoutOptions.Center,
        };
        foreach (var card in cards)
        {
            column.Children.Add(card);
        }

        var grid = new Grid
        {
            RowSpacing = 0,
            BackgroundColor = Col("AfPageBackground"),
        };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));    // header
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(3)));  // hazard stripe
        grid.Add(Header(eyebrow, title), 0, 0);
        grid.Add(new BoxView { Style = St("AfHazardStripe") }, 0, 1);

        var row = 2;
        if (pinnedHeader is not null)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // pinned tab strip
            grid.Add(CenteredBand(pinnedHeader, maxWidth), 0, row);
            row++;
        }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));     // scrollable cards
        grid.Add(new ScrollView { Content = column }, 0, row);
        row++;
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));     // footer
        grid.Add(footer, 0, row);
        return grid;
    }

    /// <summary>
    /// Scaffold variant with a vertical tab rail down the left edge instead of a pinned strip
    /// across the top: branded header + hazard stripe span the full width, then a two-column
    /// body (the <paramref name="sidebar"/> rail beside the scrollable <paramref name="body"/>),
    /// then the sticky <paramref name="footer"/>. Used by the settings sheet so its many sections
    /// read as left-hand tabs rather than a cramped horizontal strip.
    /// </summary>
    public static Grid ScaffoldWithSidebar(string eyebrow, string title, View sidebar, View body, View footer, double contentMaxWidth = 640)
    {
        var column = new VerticalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(24, 22, 24, 28),
            MaximumWidthRequest = contentMaxWidth,
            // Centred in the area to the right of the rail, so the cards aren't stranded
            // against the sidebar with a wide empty gap on the right.
            HorizontalOptions = LayoutOptions.Center,
            Children = { body },
        };

        var middle = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto), // tab rail
                new ColumnDefinition(GridLength.Star), // scrollable cards
            },
        };
        middle.Add(new Border
        {
            BackgroundColor = Col("AfPanelElevated"),
            Stroke = new SolidColorBrush(Col("AfDivider")),
            StrokeThickness = 0,
            Padding = new Thickness(12, 18),
            Content = sidebar,
        }, 0, 0);
        middle.Add(new ScrollView { Content = column }, 1, 0);

        var grid = new Grid { RowSpacing = 0, BackgroundColor = Col("AfPageBackground") };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // header
        grid.RowDefinitions.Add(new RowDefinition(new GridLength(3))); // hazard stripe
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));   // sidebar + cards
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // footer
        grid.Add(Header(eyebrow, title), 0, 0);
        grid.Add(new BoxView { Style = St("AfHazardStripe") }, 0, 1);
        grid.Add(middle, 0, 2);
        grid.Add(footer, 0, 3);
        return grid;
    }

    /// <summary>
    /// A vertical tab rail: one stacked, full-width item per option. The active item is filled
    /// with the accent; tapping one restyles the rail and calls <paramref name="onChange"/> with
    /// its index. Suits a sheet with several sections (settings) read as left-hand tabs.
    /// </summary>
    public static View VerticalTabRail(IReadOnlyList<string> options, int selected, Action<int> onChange, double width = 184)
    {
        var stack = new VerticalStackLayout { Spacing = 4, WidthRequest = width };
        var items = new List<(Border Border, Label Label)>();

        void Restyle(int active)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var on = i == active;
                items[i].Border.BackgroundColor = on ? Col("AfAccentOrange") : Colors.Transparent;
                items[i].Label.TextColor = on ? Col("AfTextOnAccent") : Col("AfTextSecondary");
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            var label = new Label
            {
                Text = options[i],
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                CharacterSpacing = 1,
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalTextAlignment = TextAlignment.Center,
            };
            var item = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(14, 10),
                Content = label,
            };
            var index = i;
            item.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { Restyle(index); onChange(index); }) });
            items.Add((item, label));
            stack.Children.Add(item);
        }

        Restyle(selected);
        return stack;
    }

    /// <summary>
    /// Wraps a pinned band (e.g. the settings tab strip) so it lines up with the centred,
    /// max-width card column below it: same horizontal padding, capped width, page-coloured
    /// so scrolling cards don't show through.
    /// </summary>
    private static View CenteredBand(View content, double maxWidth)
    {
        // A Center/MaximumWidthRequest container shrink-wraps to the content's own width, which
        // collapses a short tab strip (truncating its wider labels). A 3-column grid with a fixed
        // centre column pins the band to exactly the card width (the flanking star columns centre
        // it), so a Fill child - the tab strip - stretches the full width with even segments.
        content.HorizontalOptions = LayoutOptions.Fill;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(maxWidth)),
                new ColumnDefinition(GridLength.Star),
            },
        };
        grid.Add(content, 1, 0);
        return new ContentView
        {
            BackgroundColor = Col("AfPageBackground"),
            Padding = new Thickness(24, 12),
            Content = grid,
        };
    }

    private static View Header(string eyebrow, string title)
    {
        var badge = new Border
        {
            BackgroundColor = Col("AfAccentOrange"),
            StrokeThickness = 0,
            WidthRequest = 36,
            HeightRequest = 36,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "AF",
                TextColor = Col("AfTextOnAccent"),
                FontFamily = "OpenSansSemibold",
                FontSize = 14,
                CharacterSpacing = 2,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            },
        };

        var text = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = eyebrow, Style = St("AfFieldLabel"), TextColor = Col("AfAccentOrange"), CharacterSpacing = 3 },
                new Label { Text = title, Style = St("AfH1"), FontSize = 22 },
            },
        };

        return new Border
        {
            Style = St("AfChrome"),
            Padding = new Thickness(24, 16),
            Content = new HorizontalStackLayout { Spacing = 14, Children = { badge, text } },
        };
    }

    /// <summary>A titled facility panel card: amber section label, optional hint, then body.</summary>
    public static Border Card(string header, string? hint, params View[] body)
    {
        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Children.Add(new Label
        {
            Text = header,
            Style = St("AfFieldLabel"),
            TextColor = Col("AfAmber"),
            CharacterSpacing = 3,
        });
        if (!string.IsNullOrEmpty(hint))
        {
            stack.Children.Add(new Label { Text = hint, Style = St("AfMuted"), FontSize = 11 });
        }
        foreach (var v in body)
        {
            stack.Children.Add(v);
        }
        return new Border { Style = St("AfPanel"), Padding = new Thickness(20, 16), Content = stack };
    }

    /// <summary>A right-aligned footer action bar in the header chrome colour.</summary>
    public static View Footer(params View[] actions)
    {
        var row = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
        };
        foreach (var a in actions)
        {
            row.Children.Add(a);
        }
        return new Border { Style = St("AfChrome"), Padding = new Thickness(20, 12), Content = row };
    }

    /// <summary>An eyebrow sub-label inside a card (e.g. "A (first / old)").</summary>
    public static Label SubLabel(string text) => new()
    {
        Text = text,
        Style = St("AfFieldLabel"),
        TextColor = Col("AfTextSecondary"),
        CharacterSpacing = 2,
    };

    /// <summary>Default (primary) button, optionally a ghost/secondary one.</summary>
    public static Button Button(string text, bool primary = true)
    {
        var b = new Button { Text = text };
        if (!primary)
        {
            b.Style = St("AfGhostButton");
        }
        return b;
    }

    /// <summary>
    /// A proper segmented toggle (a bordered pill split into N tappable segments; the active
    /// one is filled). Calls <paramref name="onChange"/> with the chosen index. By default the
    /// pill hugs its content (left-aligned), which suits a 2-3 option toggle; pass
    /// <paramref name="fill"/> to stretch it full-width with equal segments, which suits a
    /// many-item tab strip.
    /// </summary>
    public static View Segmented(IReadOnlyList<string> options, int selected, Action<int> onChange, bool fill = false)
    {
        var grid = new Grid { ColumnSpacing = 3 };
        var segments = new List<(Border Border, Label Label)>();

        void Restyle(int active)
        {
            for (var i = 0; i < segments.Count; i++)
            {
                var on = i == active;
                segments[i].Border.BackgroundColor = on ? Col("AfAccentOrange") : Colors.Transparent;
                segments[i].Label.TextColor = on ? Col("AfTextOnAccent") : Col("AfTextSecondary");
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            // Fill: equal star columns so the strip spans the sheet (a many-tab bar). Otherwise
            // Auto columns so each segment sizes to its own label - star columns under a
            // shrink-to-content (Start-aligned) pill collapse and truncate the text.
            grid.ColumnDefinitions.Add(new ColumnDefinition(fill ? GridLength.Star : GridLength.Auto));
            var label = new Label
            {
                Text = options[i],
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                CharacterSpacing = 1,
                // Full-width segments can run out of room (truncate); content-sized ones never do.
                LineBreakMode = fill ? LineBreakMode.TailTruncation : LineBreakMode.NoWrap,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };
            // A full-width strip gives each segment an equal star column, so trim the per-segment
            // padding (it would otherwise force the pill wider than the sheet with many tabs).
            var seg = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(fill ? 8 : 14, 7),
                Content = label,
            };
            var index = i;
            seg.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { Restyle(index); onChange(index); }) });
            segments.Add((seg, label));
            grid.Add(seg, i, 0);
        }

        Restyle(selected);
        return new Border
        {
            BackgroundColor = Col("AfPanelElevated"),
            Stroke = new SolidColorBrush(Col("AfDivider")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 3,
            HorizontalOptions = fill ? LayoutOptions.Fill : LayoutOptions.Start,
            Content = grid,
        };
    }
}
