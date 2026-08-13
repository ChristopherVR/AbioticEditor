using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.GamePass;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The things a player has to be told before editing a Game Pass save, and the one repair the
/// editor can offer them.
/// </summary>
/// <remarks>
/// <para>Unlike every other save the editor touches, a Game Pass save is not the only copy: Xbox
/// keeps one in the cloud and arbitrates between them, and the cloud copy can win. An edit written
/// here can therefore be silently thrown away hours later, with nothing on screen having gone
/// wrong at the time. The only reliable defence is a workflow the player has to follow themselves
/// (close the game and the Xbox app, go offline, edit, launch the game once offline, only then
/// reconnect), so the editor's job is to explain it before the first edit rather than after the
/// loss. Shown once per run: it is long, and a player working through several worlds should not
/// have to dismiss it each time.</para>
///
/// <para>The second case is a save whose container list points at data that is not on disk - the
/// fingerprint of an Xbox sync that never finished. The save still opens (the editor finds the
/// real data), but the folder is inconsistent, and writing into it in that state is exactly what
/// leads Xbox to drop the save later. That one is repairable, so it is offered as a repair rather
/// than a warning.</para>
///
/// <para>The third case is everything the write guard can see that is not about the folder at all:
/// most of the time, Abiotic Factor still being open. That one is said at OPEN rather than at SAVE
/// on purpose. A player who is told after an hour of editing that the game had to be closed the
/// whole time has already done the work twice.</para>
/// </remarks>
public sealed class GamePassSafetyGuard(
    SaveWorkspaceSessionService workspace, ModalService modals, HostLanguageService language, ToastService toasts)
{
    private bool _cloudSyncWarningShown;

    /// <summary>
    /// Runs <paramref name="open"/>, first explaining the cloud-sync workflow when
    /// <paramref name="folder"/> is a Game Pass save and this run has not explained it yet, and
    /// afterwards offering to repair a half-synced folder.
    /// </summary>
    /// <remarks>
    /// Like <see cref="UnsavedChangesGuard.ConfirmAsync"/>, this returns as soon as a question is
    /// on screen; the open happens later, through the dialog's confirm. Callers must not assume
    /// the world is open once this has been awaited.
    /// </remarks>
    public Task OpenAsync(string? folder, Func<Task> open)
    {
        ArgumentNullException.ThrowIfNull(open);

        if (!IsGamePassFolder(folder))
        {
            return open();
        }

        if (_cloudSyncWarningShown)
        {
            return OfferRepairThenOpenAsync(folder!, open);
        }

        _cloudSyncWarningShown = true;
        modals.Show(new ModalRequest(
            language.Resource("Main_GpCloudSyncWarningTitle"),
            Paragraphs(language.Resource("Main_GpCloudSyncWarningMessage")),
            ConfirmText: language.Resource("Main_GpCloudSyncWarningContinue"),
            OnConfirm: () => OfferRepairThenOpenAsync(folder!, open),
            CancelText: language.Resource("Common_Cancel")));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks about a repair before the world opens, and only when there is something to repair.
    /// </summary>
    /// <remarks>
    /// Both halves of that matter. Asking afterwards left the player looking at an open world while
    /// being offered the choice of opening it, and asking when nothing was wrong ended in "fixed 0
    /// saves", which is how a prompt teaches people to click past it. The check reads the container
    /// list and looks for the data files; it never unpacks a world, so it is quick enough to happen
    /// before the open rather than after.
    /// </remarks>
    private Task OfferRepairThenOpenAsync(string folder, Func<Task> open)
    {
        IReadOnlyList<string> repairable;
        try
        {
            repairable = GamePassSaveSet.PartsNeedingRepair(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return open();
        }

        if (repairable.Count == 0) return open();

        EditorLog.Warn("GamePass",
            $"'{folder}' has {repairable.Count} part(s) a repair would put right: {string.Join(", ", repairable)}");
        modals.Show(new ModalRequest(
            language.Resource("Main_GpMidSyncTitle"),
            Paragraphs(language.Resource("Main_GpMidSyncMessage", repairable.Count)),
            ConfirmText: language.Resource("Main_GpMidSyncRepair"),
            OnConfirm: () => RepairThenOpenAsync(folder, open),
            CancelText: language.Resource("Main_GpMidSyncSkip"),
            OnCancel: open));
        return Task.CompletedTask;
    }

    private async Task RepairThenOpenAsync(string folder, Func<Task> open)
    {
        try
        {
            var repaired = await Task.Run(() => GamePassSaveSet.Open(folder).RepairMidSync()).ConfigureAwait(false);
            toasts.Show(language.Resource("Main_GpMidSyncRepaired", repaired.Count), ToastKind.Success);
        }
        catch (Exception ex)
        {
            EditorLog.Error("GamePass", "Repairing a Game Pass save failed", ex);
            toasts.Show(language.Resource("Main_GpRepairFailed"), ToastKind.Error);
        }
        await open().ConfigureAwait(false);
    }

    /// <summary>
    /// Says what the world that just opened is going to be like to save, while the player still has
    /// the chance to act on it: a repair the editor can do itself, or a reason only they can clear.
    /// </summary>
    /// <remarks>
    /// <para>At most one dialog, and only for something that would refuse a save. The warnings the
    /// guard also reports are led by "the Xbox app is running", which on a Game Pass machine is
    /// true essentially always - a dialog for that would be dismissed unread every single time, and
    /// would take the blocking cases down with it.</para>
    ///
    /// <para>Repair comes first when it applies, because a folder left half-synced is both the
    /// cause of the refusal and the thing the editor can actually fix.</para>
    /// </remarks>
    public void ExplainSaveState()
    {
        if (workspace.Current?.GamePass?.Set is not { } set) return;
        var check = workspace.GamePassWriteState();

        if (set.IsMidSync || set.NeedsAttention)
        {
            EditorLog.Warn("GamePass",
                $"Opened a save needing attention (mid-sync: {set.IsMidSync}, "
                + $"unresolved conflict: {set.HasUnresolvedConflicts}, "
                + $"recovered: {string.Join(", ", set.RecoveredContainers)}, "
                + $"bad state: {string.Join(", ", set.InvalidStateContainers)}).");
            modals.Show(new ModalRequest(
                language.Resource("Main_GpMidSyncTitle"),
                Paragraphs(language.Resource("Main_GpMidSyncMessage"), check?.Lines()),
                ConfirmText: language.Resource("Main_GpMidSyncRepair"),
                OnConfirm: RepairAsync,
                CancelText: language.Resource("Main_GpMidSyncSkip")));
            return;
        }

        if (check is null || check.CanWrite) return;

        EditorLog.Warn("GamePass",
            $"Opened a save that cannot be written yet: {string.Join(" ", check.Lines())}");
        modals.Show(new ModalRequest(
            language.Resource("Main_GpNotWritableTitle"),
            Paragraphs(
                language.Resource("Main_GpNotWritableMessage"),
                check.Lines(),
                language.Resource("Main_GpNotWritableFix")),
            CancelText: language.Resource("Common_Close")));
    }

    /// <summary>Repairs the open save, then says how much of it was put right.</summary>
    public async Task RepairAsync()
    {
        if (workspace.Current?.GamePass is null) return;
        try
        {
            var repaired = await GamePassRecovery.RepairAsync(workspace).ConfigureAwait(false);
            toasts.Show(language.Resource("Main_GpMidSyncRepaired", repaired.Count), ToastKind.Success);
        }
        catch (Exception ex)
        {
            EditorLog.Error("GamePass", "Repairing a half-synced save failed", ex);
            toasts.Show(language.Resource("Main_GpRepairFailed"), ToastKind.Error);
        }
    }

    private static bool IsGamePassFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try { return GamePassSaveSet.IsGamePassFolder(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>
    /// Renders the warning text as paragraphs, optionally followed by a list of what the write
    /// guard found and a closing paragraph. The warnings are numbered, multi-line procedures, and
    /// collapsing one into a run-on block is how a set of steps stops reading as steps; the guard's
    /// findings are separate facts, so they get a list rather than being run together.
    /// </summary>
    private static RenderFragment Paragraphs(
        string text, IReadOnlyList<string>? findings = null, string? closing = null) => builder =>
    {
        var sequence = 0;
        WriteParagraphs(builder, ref sequence, text);

        if (findings is { Count: > 0 })
        {
            builder.OpenElement(sequence++, "ul");
            foreach (var finding in findings)
            {
                builder.OpenElement(sequence++, "li");
                builder.AddContent(sequence++, finding);
                builder.CloseElement();
            }
            builder.CloseElement();
        }

        if (!string.IsNullOrWhiteSpace(closing)) WriteParagraphs(builder, ref sequence, closing);
    };

    private static void WriteParagraphs(RenderTreeBuilder builder, ref int sequence, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) continue;
            builder.OpenElement(sequence++, "p");
            builder.AddContent(sequence++, line);
            builder.CloseElement();
        }
    }
}
