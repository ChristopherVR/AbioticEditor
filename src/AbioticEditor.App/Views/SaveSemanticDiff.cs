using AbioticEditor.App.Services;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace AbioticEditor.App.Views;

/// <summary>One named thing that differs (a recipe, fish, item, trait…) with its display
/// name and - when resolvable - the item-catalog entry whose icon represents it.</summary>
internal sealed record SemanticItem(string Id, string DisplayName, ItemCatalogEntry? Icon);

/// <summary>A scalar that changed between the two saves (money, a skill level…).</summary>
internal sealed record SemanticScalar(string Label, string A, string B);

/// <summary>One comparison category (Recipes, Fish, Progression…).</summary>
internal sealed class SemanticSection
{
    public required string Title { get; init; }
    public List<SemanticScalar> Scalars { get; } = new();
    public List<SemanticItem> OnlyA { get; } = new();
    public List<SemanticItem> OnlyB { get; } = new();

    public bool HasContent => Scalars.Count > 0 || OnlyA.Count > 0 || OnlyB.Count > 0;

    public string Summary => Scalars.Count > 0 && OnlyA.Count == 0 && OnlyB.Count == 0
        ? $"{Scalars.Count} value(s) changed"
        : $"{OnlyA.Count} only in A · {OnlyB.Count} only in B";
}

/// <summary>
/// Builds a human-readable, domain-aware diff of two PLAYER saves - "save A has Fish X,
/// save B doesn't" rather than raw property paths - reusing the editor's catalogs for
/// display names and icons. The raw property diff stays available as a deep-dive.
/// </summary>
internal static class PlayerSemanticDiff
{
    public static List<SemanticSection> Build(PlayerSaveData a, PlayerSaveData b)
    {
        var catalog = GameDataServices.Catalog;
        var sections = new List<SemanticSection>();

        SemanticItem ByItem(string id)
        {
            var e = catalog?.Find(id);
            return new SemanticItem(id, e?.DisplayName ?? Pretty(id), e);
        }

        SemanticItem ByRecipe(string id)
        {
            var recipe = GameDataServices.AllRecipeInfos.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            var e = recipe?.CreatesItemId is { } itemId ? catalog?.Find(itemId) : null;
            return new SemanticItem(id, e?.DisplayName ?? Pretty(id), e);
        }

        SemanticItem ByFish(string id)
        {
            var fish = GameDataServices.AllFish.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
            var e = fish?.ItemId is { } itemId ? catalog?.Find(itemId) : null;
            return new SemanticItem(id, e?.DisplayName ?? Pretty(id), e);
        }

        SemanticItem ByTrait(string id)
        {
            var name = GameDataServices.TraitDetails.TryGetValue(id, out var d) ? d.DisplayName : Pretty(id);
            return new SemanticItem(id, name, null);
        }

        SemanticItem Plain(string id) => new(id, Pretty(id), null);

        // ----- PROGRESSION: money + per-skill level -----
        var progression = new SemanticSection { Title = "Progression" };
        if (a.Stats.Money != b.Stats.Money)
        {
            progression.Scalars.Add(new SemanticScalar("Money",
                a.Stats.Money.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
                b.Stats.Money.ToString("N0", System.Globalization.CultureInfo.CurrentCulture)));
        }
        var skillNames = GameDataServices.SkillDefinitions.ToDictionary(s => s.SaveIndex, s => s.DisplayName);
        var skillCount = Math.Min(a.Skills.Count, b.Skills.Count);
        for (var i = 0; i < skillCount; i++)
        {
            var la = a.Skills[i].Level;
            var lb = b.Skills[i].Level;
            if (la != lb)
            {
                var name = skillNames.TryGetValue(i, out var n) ? n : $"Skill {i + 1}";
                progression.Scalars.Add(new SemanticScalar(name, $"Lv {la}", $"Lv {lb}"));
            }
        }
        AddIf(sections, progression);

        // ----- set-difference categories -----
        sections.Add(SetSection("Recipes unlocked", a.Recipes, b.Recipes, ByRecipe));
        sections.Add(SetSection("Fish caught", a.FishCaught, b.FishCaught, ByFish));
        sections.Add(SetSection("Traits", a.Traits, b.Traits, ByTrait));
        sections.Add(SetSection("Items discovered", a.ItemsPickedUp, b.ItemsPickedUp, ByItem));
        sections.Add(SetSection("Items crafted", a.CraftedItems, b.CraftedItems, ByItem));
        sections.Add(SetSection("Maps unlocked", a.MapsUnlocked, b.MapsUnlocked, Plain));
        sections.Add(SetSection("Journal entries", a.Journals, b.Journals, Plain));
        sections.Add(SetSection("Emails read", a.EmailsRead, b.EmailsRead, Plain));

        return sections.Where(s => s.HasContent).ToList();
    }

    private static SemanticSection SetSection(
        string title, IReadOnlyList<string> a, IReadOnlyList<string> b, Func<string, SemanticItem> resolve)
    {
        var section = new SemanticSection { Title = title };
        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        section.OnlyA.AddRange(a.Where(x => !setB.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resolve).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        section.OnlyB.AddRange(b.Where(x => !setA.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resolve).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        return section;
    }

    private static void AddIf(List<SemanticSection> sections, SemanticSection s)
    {
        if (s.HasContent) sections.Add(s);
    }

    /// <summary>Fallback display name for an unresolved id: "recipe_first_aid" -> "first aid".</summary>
    private static string Pretty(string id)
    {
        var s = id;
        foreach (var prefix in new[] { "recipe_", "srecipe_", "crecipe_", "Trait_" })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { s = s[prefix.Length..]; break; }
        }
        return s.Replace('_', ' ').Trim();
    }

    // ===== rendering =====

    private static Color Col(string key) => (Color)Application.Current!.Resources[key];

    /// <summary>One section rendered as a facility card: summary, scalar rows, A/B chip groups.</summary>
    public static View RenderSection(SemanticSection section)
    {
        var body = new List<View>
        {
            new Label { Text = section.Summary, Style = (Style)Application.Current!.Resources["AfMuted"], FontSize = 11 },
        };

        foreach (var s in section.Scalars)
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(1.4, GridUnitType.Star)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                },
                ColumnSpacing = 8,
            };
            grid.Add(new Label { Text = s.Label, Style = (Style)Application.Current!.Resources["AfFieldValue"], FontSize = 12, VerticalOptions = LayoutOptions.Center }, 0, 0);
            grid.Add(new Label { Text = s.A, FontSize = 12, TextColor = Col("AfAlertRed"), HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center }, 1, 0);
            grid.Add(new Label { Text = "→", FontSize = 12, TextColor = Col("AfTextMuted"), VerticalOptions = LayoutOptions.Center }, 2, 0);
            grid.Add(new Label { Text = s.B, FontSize = 12, TextColor = Col("AfTerminalGreen"), VerticalOptions = LayoutOptions.Center }, 3, 0);
            body.Add(grid);
        }

        if (section.OnlyA.Count > 0)
        {
            body.Add(ChipGroup("ONLY IN A", section.OnlyA, Col("AfAlertRed")));
        }
        if (section.OnlyB.Count > 0)
        {
            body.Add(ChipGroup("ONLY IN B", section.OnlyB, Col("AfTerminalGreen")));
        }

        return ModalChrome.Card(section.Title.ToUpperInvariant(), null, body.ToArray());
    }

    private static View ChipGroup(string heading, IReadOnlyList<SemanticItem> items, Color accent)
    {
        var flex = new FlexLayout { Wrap = FlexWrap.Wrap, Direction = FlexDirection.Row };
        foreach (var item in items)
        {
            var chip = BuildChip(item, accent);
            chip.Margin = new Thickness(0, 0, 8, 8);
            flex.Children.Add(chip);
        }
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label { Text = heading, Style = (Style)Application.Current!.Resources["AfFieldLabel"], TextColor = accent, CharacterSpacing = 2 },
                flex,
            },
        };
    }

    private static View BuildChip(SemanticItem item, Color accent)
    {
        var image = new Image { WidthRequest = 22, HeightRequest = 22, Aspect = Aspect.AspectFit, IsVisible = false, VerticalOptions = LayoutOptions.Center };
        StartIconExtraction(item, image);

        var dot = new BoxView { WidthRequest = 6, HeightRequest = 6, CornerRadius = 3, Color = accent, VerticalOptions = LayoutOptions.Center, IsVisible = item.Icon is null };

        var row = new HorizontalStackLayout
        {
            Spacing = 7,
            Children =
            {
                dot,
                image,
                new Label { Text = item.DisplayName, FontSize = 12, TextColor = Col("AfTextPrimary"), VerticalOptions = LayoutOptions.Center },
            },
        };
        return new Border
        {
            BackgroundColor = Col("AfPanelElevated"),
            Stroke = new SolidColorBrush(Col("AfBorderSubtle")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(9, 5),
            Content = row,
        };
    }

    private static void StartIconExtraction(SemanticItem item, Image target)
    {
        var entry = item.Icon;
        var provider = GameDataServices.Provider;
        if (entry?.IconAssetPath is not { } path || provider is null) return;

        _ = Task.Run(() =>
        {
            try
            {
                var raw = provider.ExtractTextureByGameRef(path);
                var colorized = raw is null ? null : IconColorizer.Colorize(raw, entry);
                if (colorized is not null)
                {
                    MainThread.BeginInvokeOnMainThread(() => { target.Source = colorized; target.IsVisible = true; });
                }
            }
            catch
            {
                // Icons are cosmetic.
            }
        });
    }
}

/// <summary>
/// Domain-aware diff of two WORLD saves (per-region or metadata) - story chapter, quest
/// flags, doors, ground items, NPCs and world-object counts - mirroring the world editor's
/// tabs. Renders through <see cref="PlayerSemanticDiff.RenderSection"/>.
/// </summary>
internal static class WorldSemanticDiff
{
    public static List<SemanticSection> Build(WorldSaveData a, WorldSaveData b)
    {
        var catalog = GameDataServices.Catalog;
        var sections = new List<SemanticSection>();

        SemanticItem ByItem(string id)
        {
            var e = catalog?.Find(id);
            return new SemanticItem(id, e?.DisplayName ?? Pretty(id), e);
        }

        SemanticItem ByRecipe(string id)
        {
            var recipe = GameDataServices.AllRecipeInfos.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            var e = recipe?.CreatesItemId is { } itemId ? catalog?.Find(itemId) : null;
            return new SemanticItem(id, e?.DisplayName ?? Pretty(id), e);
        }

        SemanticItem ByFlag(string raw)
        {
            var info = QuestFlagCatalog.Lookup(raw);
            var name = string.IsNullOrEmpty(info.FriendlyName) ? Pretty(raw) : info.FriendlyName;
            return new SemanticItem(raw, name, null);
        }

        // ----- PROGRESSION (metadata saves carry these) -----
        var progression = new SemanticSection { Title = "Progression" };
        if (!string.Equals(a.StoryProgressionRow, b.StoryProgressionRow, StringComparison.OrdinalIgnoreCase)
            && (a.StoryProgressionRow is not null || b.StoryProgressionRow is not null))
        {
            progression.Scalars.Add(new SemanticScalar("Story chapter",
                ChapterName(a.StoryProgressionRow), ChapterName(b.StoryProgressionRow)));
        }
        if (a.MinutesPassed is { } ma && b.MinutesPassed is { } mb && ma != mb)
        {
            progression.Scalars.Add(new SemanticScalar("Time played", $"{ma / 60}h {ma % 60}m", $"{mb / 60}h {mb % 60}m"));
        }
        if (progression.HasContent) sections.Add(progression);

        sections.Add(SetSection("Global recipes", a.GlobalRecipes, b.GlobalRecipes, ByRecipe));

        // ----- quest flags -----
        sections.Add(SetSection("Quest flags", a.Flags, b.Flags, ByFlag));

        // ----- doors that changed lock/open state (matched by id) -----
        var doors = new SemanticSection { Title = "Doors" };
        var doorsB = b.Doors.ToDictionary(d => d.Id, d => d, StringComparer.OrdinalIgnoreCase);
        foreach (var da in a.Doors)
        {
            if (!doorsB.TryGetValue(da.Id, out var db)) continue;
            var sa = DoorState(da);
            var sb = DoorState(db);
            if (!string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
            {
                doors.Scalars.Add(new SemanticScalar(Short(da.Id), sa, sb));
            }
        }
        if (doors.HasContent) sections.Add(doors);

        // ----- ground items present in one save but not the other (aggregated by item) -----
        sections.Add(SetSection("Ground items",
            a.DroppedItems.Select(d => d.Slot.ItemId).Where(NotEmpty).ToList()!,
            b.DroppedItems.Select(d => d.Slot.ItemId).Where(NotEmpty).ToList()!,
            ByItem));

        // ----- NPCs whose state changed (matched by id) -----
        var npcs = new SemanticSection { Title = "NPCs" };
        var npcsB = b.Npcs.ToDictionary(n => n.Id, n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var na in a.Npcs)
        {
            if (!npcsB.TryGetValue(na.Id, out var nb)) continue;
            var sa = NpcState(na);
            var sb = NpcState(nb);
            if (!string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
            {
                npcs.Scalars.Add(new SemanticScalar(na.ActorName, sa, sb));
            }
        }
        if (npcs.HasContent) sections.Add(npcs);

        // ----- world-object counts -----
        var contents = new SemanticSection { Title = "World contents" };
        AddCountScalar(contents, "Containers", a.Containers.Count, b.Containers.Count);
        AddCountScalar(contents, "Placed objects", a.Deployables.Count, b.Deployables.Count);
        AddCountScalar(contents, "Ground items", a.DroppedItems.Count, b.DroppedItems.Count);
        AddCountScalar(contents, "Tracked NPCs", a.Npcs.Count, b.Npcs.Count);
        if (contents.HasContent) sections.Add(contents);

        return sections.Where(s => s.HasContent).ToList();
    }

    private static bool NotEmpty(string? id) => !string.IsNullOrEmpty(id) && id is not ("None" or "Empty");

    private static void AddCountScalar(SemanticSection s, string label, int a, int b)
    {
        if (a != b) s.Scalars.Add(new SemanticScalar(label, a.ToString(System.Globalization.CultureInfo.InvariantCulture), b.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string ChapterName(string? row)
        => row is null ? "(none)" : StoryProgressionCatalog.Find(row)?.Title ?? row;

    private static string DoorState(WorldDoor d)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(d.DoorState)) parts.Add(d.DoorState!);
        if (d.OneWayUnlocked == true) parts.Add("one-way unlocked");
        if (d.IsDoorOpen == true) parts.Add("open");
        return parts.Count > 0 ? string.Join(", ", parts) : "-";
    }

    private static string NpcState(WorldNpc n)
        => n.IsDead ? "dead" : string.IsNullOrEmpty(n.State) ? "alive" : n.State!;

    private static string Short(string id)
    {
        var dot = id.LastIndexOf('.');
        return dot >= 0 ? id[(dot + 1)..] : id;
    }

    private static SemanticSection SetSection(
        string title, IReadOnlyList<string> a, IReadOnlyList<string> b, Func<string, SemanticItem> resolve)
    {
        var section = new SemanticSection { Title = title };
        var setA = new HashSet<string>(a, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(b, StringComparer.OrdinalIgnoreCase);
        section.OnlyA.AddRange(a.Where(x => !setB.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resolve).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        section.OnlyB.AddRange(b.Where(x => !setA.Contains(x)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resolve).OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase));
        return section;
    }

    private static string Pretty(string id)
    {
        var s = id;
        foreach (var prefix in new[] { "recipe_", "srecipe_", "crecipe_" })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { s = s[prefix.Length..]; break; }
        }
        return s.Replace('_', ' ').Trim();
    }
}
