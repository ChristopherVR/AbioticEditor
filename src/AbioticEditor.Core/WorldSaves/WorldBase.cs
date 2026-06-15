namespace AbioticEditor.Core.WorldSaves;

/// <summary>
/// One player-placed object from <c>DeployedObjectMap</c>, position included.
/// Unlike <see cref="WorldContainer"/> this covers every deployable - benches,
/// furniture, defenses - not just ones with inventories.
/// </summary>
public sealed record WorldDeployable(
    string Id,
    string? ClassName,
    double X,
    double Y,
    double Z,
    bool HasInventory,
    int StoredItemCount,
    string? CustomName = null,
    IReadOnlyList<string>? Upgrades = null)
{
    /// <summary>
    /// Installed bench upgrade rows (e.g. <c>TougherBench</c>) read from the deployable's
    /// gameplay-tag container; empty for deployables with no upgrades. See
    /// <see cref="BenchUpgradeCatalog"/>.
    /// </summary>
    public IReadOnlyList<string> InstalledUpgrades => Upgrades ?? Array.Empty<string>();
    /// <summary>
    /// Separator the game embeds in a bed's <c>CustomTextDisplay_</c> when claiming:
    /// <c>&lt;steamid64&gt;}|!|{&lt;playerName&gt;</c>. An unclaimed bed carries the bare
    /// separator (both halves empty).
    /// </summary>
    public const string ClaimSeparator = "}|!|{";

    public string FriendlyClass
    {
        get
        {
            var name = ClassName ?? Id;
            // Only the trailing "_C" is the blueprint-class suffix - a blanket replace
            // would eat the C of "_CraftedBed" etc.
            if (name.EndsWith("_C", StringComparison.Ordinal)) name = name[..^2];
            return name.Replace("Deployed_", "").Replace("Deployable_", "").Replace('_', ' ');
        }
    }

    /// <summary>
    /// Player-given name (<c>CustomTextDisplay_</c>) when set, else the class name.
    /// Bed-claim strings (<c>steamid}|!|{name</c>) render as a claim description rather
    /// than the raw separator soup.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CustomName)) return FriendlyClass;
            if (HasClaimMarker)
            {
                return OwnerName is { Length: > 0 } owner
                    ? $"{FriendlyClass} (claimed by {owner})"
                    : $"{FriendlyClass} (unclaimed)";
            }
            return $"“{CustomName}” ({FriendlyClass})";
        }
    }

    public bool IsCraftingBench =>
        ClassName?.Contains("CraftingBench", StringComparison.OrdinalIgnoreCase) == true
        || ClassName?.Contains("Bench_Crafting", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True for claimable beds: crafted player beds and pet beds.</summary>
    public bool IsBed =>
        ClassName?.Contains("CraftedBed", StringComparison.OrdinalIgnoreCase) == true
        || ClassName?.Contains("PetBed", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Pet beds are claimable but cannot be a player spawn point.</summary>
    public bool IsPetBed => ClassName?.Contains("PetBed", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when <see cref="CustomName"/> carries the bed-claim separator.</summary>
    public bool HasClaimMarker => CustomName?.Contains(ClaimSeparator, StringComparison.Ordinal) == true;

    /// <summary>SteamID64 of the claiming player; null when unclaimed or not a claim string.</summary>
    public ulong? OwnerSteamId => ParseClaim(CustomName).SteamId;

    /// <summary>Display name of the claiming player; null when unclaimed or not a claim string.</summary>
    public string? OwnerName => ParseClaim(CustomName).Name;

    /// <summary>
    /// Parses a bed-claim string of the form <c>&lt;steamid64&gt;}|!|{&lt;name&gt;</c>.
    /// Returns (null, null) when <paramref name="customText"/> has no separator or the
    /// halves are empty (unclaimed bed). Some player names in the fixture saves carry
    /// invisible private-use-area glyphs (e.g. U+E110 wrapping the name - Steam/in-game
    /// styling artifacts); those are stripped so the name compares and displays cleanly.
    /// </summary>
    public static (ulong? SteamId, string? Name) ParseClaim(string? customText)
    {
        if (string.IsNullOrEmpty(customText)) return (null, null);
        var idx = customText.IndexOf(ClaimSeparator, StringComparison.Ordinal);
        if (idx < 0) return (null, null);

        var idPart = customText[..idx];
        var namePart = SanitizeName(customText[(idx + ClaimSeparator.Length)..]);
        ulong? steamId = ulong.TryParse(idPart, out var id) ? id : null;
        return (steamId, namePart.Length > 0 ? namePart : null);
    }

    private static string SanitizeName(string raw)
    {
        Span<char> buffer = stackalloc char[raw.Length];
        var n = 0;
        foreach (var c in raw)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat is System.Globalization.UnicodeCategory.PrivateUse
                or System.Globalization.UnicodeCategory.Control) continue;
            buffer[n++] = c;
        }
        return new string(buffer[..n]).Trim();
    }
}

/// <summary>
/// A detected player base: a crafting bench plus everything deployed near it.
/// </summary>
public sealed record WorldBase(
    string Name,
    double CenterX,
    double CenterY,
    IReadOnlyList<WorldDeployable> Deployables)
{
    public int BenchCount => Deployables.Count(d => d.IsCraftingBench);
    public int ContainerCount => Deployables.Count(d => d.HasInventory);
    public int StoredItemCount => Deployables.Sum(d => d.StoredItemCount);
}

/// <summary>
/// Groups deployables into "bases": crafting benches act as anchors and everything
/// within <see cref="ClusterRadius"/> of an anchor (transitively) joins that base.
/// Unanchored deployables far from any bench are collected into an "Ungrouped" bucket.
/// </summary>
public static class BaseDetector
{
    /// <summary>Cluster radius in unreal units (100 uu = 1 m) - 30 m around a bench.</summary>
    public const double ClusterRadius = 3000;

    public static IReadOnlyList<WorldBase> Detect(IReadOnlyList<WorldDeployable> deployables)
    {
        var anchors = deployables.Where(d => d.IsCraftingBench).ToList();
        if (anchors.Count == 0)
        {
            return deployables.Count == 0
                ? Array.Empty<WorldBase>()
                : new[] { new WorldBase("All deployables (no crafting bench found)", 0, 0, deployables) };
        }

        // Union benches whose radii overlap into one base.
        var anchorGroups = new List<List<WorldDeployable>>();
        foreach (var anchor in anchors)
        {
            var group = anchorGroups.FirstOrDefault(g =>
                g.Any(a => Distance2D(a, anchor) <= ClusterRadius * 2));
            if (group is null)
            {
                group = new List<WorldDeployable>();
                anchorGroups.Add(group);
            }
            group.Add(anchor);
        }

        var bases = new List<WorldBase>();
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        var index = 1;
        foreach (var group in anchorGroups.OrderByDescending(g => g.Count))
        {
            var members = deployables
                .Where(d => !claimed.Contains(d.Id)
                            && group.Any(a => Distance2D(a, d) <= ClusterRadius))
                .ToList();
            foreach (var m in members) claimed.Add(m.Id);

            var cx = group.Average(a => a.X);
            var cy = group.Average(a => a.Y);
            bases.Add(new WorldBase($"Base {index++}", cx, cy, members));
        }

        var leftovers = deployables.Where(d => !claimed.Contains(d.Id)).ToList();
        if (leftovers.Count > 0)
        {
            bases.Add(new WorldBase("Outside any base", 0, 0, leftovers));
        }
        return bases;
    }

    private static double Distance2D(WorldDeployable a, WorldDeployable b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
