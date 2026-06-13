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
    /// of section <paramref name="cards"/>, and a sticky <paramref name="footer"/> bar.
    /// </summary>
    public static Grid Scaffold(string eyebrow, string title, IEnumerable<View> cards, View footer, double maxWidth = 760)
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
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(3)),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
            },
            RowSpacing = 0,
            BackgroundColor = Col("AfPageBackground"),
        };
        grid.Add(Header(eyebrow, title), 0, 0);
        grid.Add(new BoxView { Style = St("AfHazardStripe") }, 0, 1);
        grid.Add(new ScrollView { Content = column }, 0, 2);
        grid.Add(footer, 0, 3);
        return grid;
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
    /// one is filled). Calls <paramref name="onChange"/> with the chosen index.
    /// </summary>
    public static View Segmented(IReadOnlyList<string> options, int selected, Action<int> onChange)
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
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var label = new Label
            {
                Text = options[i],
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                CharacterSpacing = 1,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };
            var seg = new Border
            {
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(14, 7),
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
            HorizontalOptions = LayoutOptions.Start,
            Content = grid,
        };
    }
}
