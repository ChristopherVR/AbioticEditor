using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.GamePass;
using Microsoft.AspNetCore.Components;

namespace AbioticEditor.Web.Services;

/// <summary>
/// The two things a player has to be told before editing a Game Pass save, and the one repair the
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
            return OpenThenOfferRepairAsync(open);
        }

        _cloudSyncWarningShown = true;
        modals.Show(new ModalRequest(
            language.Resource("Main_GpCloudSyncWarningTitle"),
            Paragraphs(language.Resource("Main_GpCloudSyncWarningMessage")),
            ConfirmText: language.Resource("Main_GpCloudSyncWarningContinue"),
            OnConfirm: () => OpenThenOfferRepairAsync(open),
            CancelText: language.Resource("Common_Cancel")));
        return Task.CompletedTask;
    }

    private async Task OpenThenOfferRepairAsync(Func<Task> open)
    {
        await open().ConfigureAwait(false);
        OfferRepairIfMidSync();
    }

    /// <summary>
    /// Offers the repair when the world that just opened had to recover data from a half-finished
    /// sync. Declining is a real choice - the save reads correctly either way - so the dialog says
    /// what it fixes rather than insisting.
    /// </summary>
    private void OfferRepairIfMidSync()
    {
        if (workspace.Current?.GamePass?.Set is not { IsMidSync: true } set) return;

        EditorLog.Warn("GamePass",
            $"Opened a save that is mid-sync (recovered: {string.Join(", ", set.RecoveredContainers)}).");
        modals.Show(new ModalRequest(
            language.Resource("Main_GpMidSyncTitle"),
            Paragraphs(language.Resource("Main_GpMidSyncMessage")),
            ConfirmText: language.Resource("Main_GpMidSyncRepair"),
            OnConfirm: RepairAsync,
            CancelText: language.Resource("Main_GpMidSyncSkip")));
    }

    private async Task RepairAsync()
    {
        if (workspace.Current?.GamePass?.Set is not { } set) return;
        try
        {
            var repaired = await Task.Run(set.RepairMidSync).ConfigureAwait(false);
            toasts.Show(language.Resource("Main_GpMidSyncRepaired", repaired.Count), ToastKind.Success);
        }
        catch (Exception ex)
        {
            EditorLog.Error("GamePass", "Repairing a half-synced save failed", ex);
            toasts.Show(language.Resource("Main_GpWriteFailedMessage", ex.Message), ToastKind.Error);
        }
    }

    private static bool IsGamePassFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try { return GamePassSaveSet.IsGamePassFolder(folder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Renders the warning text as paragraphs. It is a numbered, multi-line procedure, and
    /// collapsing it into one run-on block is how a set of steps stops reading as steps.</summary>
    private static RenderFragment Paragraphs(string text) => builder =>
    {
        var sequence = 0;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0) continue;
            builder.OpenElement(sequence++, "p");
            builder.AddContent(sequence++, line);
            builder.CloseElement();
        }
    };
}
