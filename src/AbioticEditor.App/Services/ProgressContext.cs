using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.App.Services;

/// <summary>
/// World story progress shared with the player-side editors. Set by the shell after the
/// sibling <c>WorldSave_Facility.sav</c> is parsed; null when no world save is available
/// (then nothing is gated - the editor must stay usable standalone).
/// </summary>
public static class ProgressContext
{
    /// <summary>The world's flags (facility save), or null when unknown.</summary>
    public static IReadOnlySet<string>? WorldFlags { get; set; }

    /// <summary>Shell status-line sink for gate messages.</summary>
    public static Action<string>? Notify { get; set; }

    private static bool HasFlag(string flag)
        => WorldFlags?.Contains(flag) == true;

    /// <summary>
    /// Can a codex/email/journal row be unlocked given world progress? Row ids share the
    /// flags' area prefixes; rows in unmapped areas are never gated. With no world
    /// context everything is allowed.
    /// </summary>
    public static bool CanUnlockRow(string rowId, out string? reason)
    {
        reason = null;
        if (WorldFlags is null) return true;

        var chapter = FlagGate.RegionChapterForRowId(rowId);
        if (chapter?.TriggerFlag is null || HasFlag(chapter.TriggerFlag)) return true;

        reason = $"\"{rowId}\" belongs to {chapter.Title}, which this world hasn't reached yet " +
                 $"(missing {chapter.TriggerFlag}). Advance the story on the QUEST FLAGS tab first.";
        return false;
    }

    /// <summary>
    /// Can a recipe be unlocked? Gated only when the recipe is granted by a known email
    /// attachment whose region the world hasn't reached (the one quest->recipe link the
    /// game data actually encodes).
    /// </summary>
    public static bool CanUnlockRecipe(string recipeId, out string? reason)
    {
        reason = null;
        if (WorldFlags is null) return true;

        var email = GameDataServices.Emails
            .FirstOrDefault(e => e.AttachmentRecipes.Contains(recipeId, StringComparer.OrdinalIgnoreCase));
        if (email is null) return true;

        var chapter = FlagGate.RegionChapterForRowId(email.Id);
        if (chapter?.TriggerFlag is null || HasFlag(chapter.TriggerFlag)) return true;

        reason = $"Recipe \"{recipeId}\" is granted by email \"{email.Subject}\" in {chapter.Title}, " +
                 $"which this world hasn't reached (missing {chapter.TriggerFlag}).";
        return false;
    }
}
