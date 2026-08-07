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
/// <remarks>
/// The facility save is read through <see cref="ISaveFileSystem"/>, so the browser host reaches
/// it inside the folder the player granted instead of by walking a local path it does not have.
/// </remarks>
public sealed class RecipeProgressGateService
{
    private readonly CodexVocabularyService _codex;
    private readonly SaveWorkspaceSessionService _workspace;
    private readonly ISaveFileSystem _files;
    // Flag sets read from a file, keyed by facility path + version stamp so an outside change re-reads.
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _fileFlagCache =
        new(StringComparer.OrdinalIgnoreCase);

    private const string FacilitySaveName = "WorldSave_Facility.sav";

    public RecipeProgressGateService(
        CodexVocabularyService codex, SaveWorkspaceSessionService workspace, ISaveFileSystem files)
    {
        _codex = codex;
        _workspace = workspace;
        _files = files;
    }

    /// <summary>Forces the email vocabulary to load off the render path (mounting paks is expensive).</summary>
    public void Warm() => _ = _codex.Get();

    /// <summary>
    /// The world flags governing the given player save, or null when they cannot be
    /// determined (no sibling facility save, or it cannot be read) - null means ungated.
    /// </summary>
    public async Task<IReadOnlySet<string>?> ResolveWorldFlagsAsync(
        string? playerSavePath, CancellationToken cancellationToken = default)
    {
        var facility = FacilityPathFor(playerSavePath);
        if (facility is null) return null;

        // An open world session knows the staged flags; prefer it over the stored file.
        var session = _workspace.Current?.WorldSession ?? _workspace.TransferWorldSession;
        if (session is not null && string.Equals(session.Path, facility, StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(session.Flags, StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var stamp = await _files.GetVersionStampAsync(facility, cancellationToken).ConfigureAwait(false);
            if (stamp is null) return null;

            var key = $"{facility}|{stamp}";
            if (_fileFlagCache.TryGetValue(key, out var cached)) return cached;

            var bytes = await _files.ReadAllBytesAsync(facility, cancellationToken).ConfigureAwait(false);
            var flags = await Task.Run(
                () => (IReadOnlySet<string>)new HashSet<string>(
                    WorldSaveReader.ReadFromStream(new MemoryStream(bytes, writable: false)).Flags,
                    StringComparer.OrdinalIgnoreCase),
                cancellationToken).ConfigureAwait(false);
            _fileFlagCache[key] = flags;
            return flags;
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
    /// The facility save holding the story flags that gate this save's recipes. A save already
    /// open in the workspace answers this on any host; otherwise the layout is walked, which
    /// only the desktop can do - world saves (metadata and regions) sit beside the facility save
    /// in the world folder, player saves one level below in PlayerData.
    /// </summary>
    private string? FacilityPathFor(string? savePath)
    {
        if (string.IsNullOrWhiteSpace(savePath)) return null;

        var fromWorkspace = _workspace.Current?.Saves.FirstOrDefault(save =>
            save.Kind == SaveDocumentKind.World
            && string.Equals(save.Name, FacilitySaveName, StringComparison.OrdinalIgnoreCase));
        if (fromWorkspace is not null) return fromWorkspace.Path;

        if (!_files.HasLocalPaths) return null;

        var directory = Path.GetDirectoryName(savePath);
        if (directory is null) return null;
        if (Path.GetFileName(savePath).StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(directory, FacilitySaveName);
        var worldDir = Path.GetDirectoryName(directory);
        return worldDir is null ? null : Path.Combine(worldDir, FacilitySaveName);
    }
}
