using System.Collections.Concurrent;
using AbioticEditor.Core.Codex;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>Why a recipe unlock is blocked: the email that grants it sits in an unreached region.</summary>
public sealed record RecipeGateBlock(string RecipeId, string EmailSubject, string ChapterTitle, string TriggerFlag);

/// <summary>
/// The retired native app's recipe progress-gate rule, kept as pure logic so it can be unit
/// tested. A recipe is gated only when it is granted by a known email attachment whose region
/// the world hasn't reached (the one quest-to-recipe link the game data actually encodes).
/// With no world context (<paramref name="worldFlags"/> null) everything is allowed - the
/// editor must stay usable standalone.
/// </summary>
public static class RecipeProgressGate
{
    public static RecipeGateBlock? TryFindBlock(
        string recipeId, IReadOnlySet<string>? worldFlags, IEnumerable<EmailEntry> emails)
    {
        if (worldFlags is null) return null;

        var email = emails.FirstOrDefault(entry =>
            entry.AttachmentRecipes.Contains(recipeId, StringComparer.OrdinalIgnoreCase));
        if (email is null) return null;

        var chapter = FlagGate.RegionChapterForRowId(email.Id);
        if (chapter?.TriggerFlag is null || worldFlags.Contains(chapter.TriggerFlag)) return null;

        return new RecipeGateBlock(recipeId, email.Subject, chapter.Title, chapter.TriggerFlag);
    }
}

/// <summary>
/// Supplies world story progress to the player-side recipe editor, mirroring the retired
/// native app: the sibling <c>WorldSave_Facility.sav</c> next to the player save carries the
/// world's quest flags, and a recipe granted by an email in a region those flags haven't
/// reached refuses to unlock. A world session already open in this workspace (with its staged
/// flag edits) is preferred over re-reading the file; when neither is available the gate
/// stands down and nothing is blocked.
/// </summary>
public sealed class RecipeProgressGateService
{
    private readonly CodexVocabularyService _codex;
    private readonly SaveWorkspaceSessionService _workspace;
    // Disk-read flag sets, keyed by facility path + write stamp so an outside change re-reads.
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _fileFlagCache =
        new(StringComparer.OrdinalIgnoreCase);

    public RecipeProgressGateService(CodexVocabularyService codex, SaveWorkspaceSessionService workspace)
    {
        _codex = codex;
        _workspace = workspace;
    }

    /// <summary>Forces the email vocabulary to load off the render path (mounting paks is expensive).</summary>
    public void Warm() => _ = _codex.Get();

    /// <summary>
    /// The world flags governing the given player save, or null when they cannot be
    /// determined (no sibling facility save, or it cannot be read) - null means ungated.
    /// </summary>
    public IReadOnlySet<string>? ResolveWorldFlags(string? playerSavePath)
    {
        var facility = FacilityPathFor(playerSavePath);
        if (facility is null) return null;

        // An open world session knows the staged flags; prefer it over the file on disk.
        var session = _workspace.Current?.WorldSession ?? _workspace.TransferWorldSession;
        if (session is not null && string.Equals(session.Path, facility, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(session.Flags, StringComparer.OrdinalIgnoreCase);
        }

        if (!File.Exists(facility)) return null;
        try
        {
            var key = $"{facility}|{File.GetLastWriteTimeUtc(facility).Ticks}";
            return _fileFlagCache.GetOrAdd(key, _ =>
                new HashSet<string>(WorldSaveReader.ReadFromFile(facility).Flags, StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            // Unreadable world save: same as the native app, progress is unknown and nothing is gated.
            return null;
        }
    }

    /// <summary>The block stopping this unlock, or null when the unlock is allowed.</summary>
    public RecipeGateBlock? CheckUnlock(string recipeId, IReadOnlySet<string>? worldFlags)
        => worldFlags is null ? null : RecipeProgressGate.TryFindBlock(recipeId, worldFlags, _codex.Get().Emails);

    /// <summary>
    /// The facility save holding the story flags that gate this save's recipes. World saves
    /// (metadata and regions) sit beside it in the world folder; player saves sit one level
    /// below in PlayerData.
    /// </summary>
    private static string? FacilityPathFor(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath)) return null;
        var directory = Path.GetDirectoryName(savePath);
        if (directory is null) return null;
        if (Path.GetFileName(savePath).StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(directory, "WorldSave_Facility.sav");
        var worldDir = Path.GetDirectoryName(directory);
        return worldDir is null ? null : Path.Combine(worldDir, "WorldSave_Facility.sav");
    }
}
