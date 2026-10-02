namespace AbioticEditor.Core.Items;

/// <summary>
/// A gameplay-tag query as the game stores it (an Unreal <c>FGameplayTagQuery</c>): a tag
/// dictionary plus a compact token stream. The game uses one on every inventory component
/// (<c>ContainerTagRequirement</c>) to decide which items a container accepts, e.g.
/// <c>ANY(Item.Food)</c> for a fridge, <c>ALL(Item.Fish)</c> for a fish tank, and the default
/// <c>NONE(Item.Pet)</c> for an ordinary crate. Kept as plain data so it can be evaluated (and
/// tested) without the game files.
/// </summary>
/// <param name="Tags">The query's tag dictionary (the token stream refers to tags by index).</param>
/// <param name="Tokens">The token stream: version, has-root flag, then the root expression.</param>
/// <param name="Description">The game's own readable form, e.g. <c>ANY( Item.Food )</c>.</param>
public sealed record ItemTagQuery(IReadOnlyList<string> Tags, IReadOnlyList<byte> Tokens, string? Description = null)
{
    // EGameplayTagQueryExprType
    private const byte AnyTagsMatch = 1;
    private const byte AllTagsMatch = 2;
    private const byte NoTagsMatch = 3;
    private const byte AnyExprMatch = 4;
    private const byte AllExprMatch = 5;
    private const byte NoExprMatch = 6;

    /// <summary>True when the query has no expression at all (it then places no limit).</summary>
    public bool IsEmpty => Tokens.Count < 2 || Tokens[1] == 0;

    /// <summary>
    /// True when an item carrying <paramref name="itemTags"/> satisfies the query. Tags match
    /// hierarchically, as the game's tag containers do: an item tagged <c>Item.Fish.Small</c>
    /// has <c>Item.Fish</c>. An empty query accepts everything; a stream this reader cannot
    /// follow also accepts everything, so an unexpected game change never hides items.
    /// </summary>
    public bool Matches(IEnumerable<string> itemTags)
    {
        ArgumentNullException.ThrowIfNull(itemTags);
        if (IsEmpty) return true;
        var tags = itemTags as IReadOnlyCollection<string> ?? itemTags.ToList();
        var index = 2;
        var result = Eval(tags, ref index, skip: false, out var ok);
        return !ok || result;
    }

    private bool Eval(IReadOnlyCollection<string> tags, ref int index, bool skip, out bool ok)
    {
        ok = true;
        if (index >= Tokens.Count) { ok = false; return false; }
        var type = Tokens[index++];
        switch (type)
        {
            case AnyTagsMatch:
            case AllTagsMatch:
            case NoTagsMatch:
            {
                if (index >= Tokens.Count) { ok = false; return false; }
                var count = Tokens[index++];
                bool any = false, all = true;
                for (var i = 0; i < count; i++)
                {
                    if (index >= Tokens.Count) { ok = false; return false; }
                    var tagIndex = Tokens[index++];
                    if (skip) continue;
                    if (tagIndex >= Tags.Count) { ok = false; return false; }
                    var has = HasTag(tags, Tags[tagIndex]);
                    any |= has;
                    all &= has;
                }
                return type switch
                {
                    AnyTagsMatch => any,
                    AllTagsMatch => all,
                    _ => !any,
                };
            }
            case AnyExprMatch:
            case AllExprMatch:
            case NoExprMatch:
            {
                if (index >= Tokens.Count) { ok = false; return false; }
                var count = Tokens[index++];
                bool any = false, all = true;
                for (var i = 0; i < count; i++)
                {
                    var value = Eval(tags, ref index, skip, out var childOk);
                    if (!childOk) { ok = false; return false; }
                    any |= value;
                    all &= value;
                }
                return type switch
                {
                    AnyExprMatch => any,
                    AllExprMatch => all,
                    _ => !any,
                };
            }
            default:
                ok = false;
                return false;
        }
    }

    /// <summary>True when <paramref name="tags"/> holds <paramref name="tag"/> or one of its children.</summary>
    public static bool HasTag(IEnumerable<string> tags, string tag)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(tag);
        foreach (var candidate in tags)
        {
            if (candidate.Equals(tag, StringComparison.OrdinalIgnoreCase)
                || (candidate.Length > tag.Length && candidate[tag.Length] == '.'
                    && candidate.StartsWith(tag, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}
