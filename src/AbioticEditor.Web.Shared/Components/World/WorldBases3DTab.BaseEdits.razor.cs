using System.Globalization;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Web.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace AbioticEditor.Web.Components.World;

/// <summary>
/// The delete, duplicate and group-edit half of the 3D view (all under the Experimental opt-in): multi-select,
/// group move/rotate/snap/align/distribute, and the delete and duplicate confirmation panels. Every edit stages
/// through the session's single <see cref="StagedBaseEdits"/>; nothing here writes a save.
/// </summary>
public partial class WorldBases3DTab
{
    /// <summary>The pivot choice that means "the centre of the selection" (any other value is an object key).</summary>
    private const string PivotCentroid = "";

    private readonly List<string> _selected = [];
    private BaseEditPreview? _basePreview;
    private string? _groupFeedback;

    // group tools
    private string _grpDx = "0", _grpDy = "0", _grpDz = "0", _grpYaw = "90", _grpPivot = PivotCentroid;
    private string _snapXY = "50", _snapZ = "", _snapYaw = "15", _distAxis = "X";

    // delete panel
    private bool _deleteOpen, _deleteBusy;
    private DeletePolicy _deletePolicy = DeletePolicy.Default;
    private List<string> _deleteKeys = [];
    private BaseEditPreview? _deletePreview;

    // duplicate panel
    private bool _dupOpen;
    private string _dupX = "200", _dupY = "0", _dupZ = "0", _dupYaw = "0", _dupPivot = PivotCentroid;
    private bool _dupCopyContents;
    private ReferencePolicy _dupPower = DuplicatePolicy.Default.ExternalPowerLinks;
    private ReferencePolicy _dupTeleporters = DuplicatePolicy.Default.ExternalTeleporterPeers;
    private List<string> _dupKeys = [];
    private BaseEditPreview? _dupPreview;
    private int _dupPreviewId = -1;
    private string? _dupError;

    private sealed record PolicyRow(
        string Id, string LabelKey, string HelpKey, ReferencePolicy Current, ReferencePolicy[] Allowed, Action<ReferencePolicy> Set);

    // ---- selection ----------------------------------------------------------------------------
    private bool IsSelected(string key) => _selected.Contains(key);

    /// <summary>The staged copy the primary selection points at, or null.</summary>
    private DuplicationPreviewRow? SelectedCopy
        => _selectedKey is null ? null : _basePreview?.Duplications.FirstOrDefault(r => string.Equals(r.NewKey, _selectedKey, StringComparison.Ordinal));

    private string SelectionLabel(string key)
    {
        if (Session.FindPlacedObject(key) is { } o) return Base3DScene.LabelOf(o);
        var copy = _basePreview?.Duplications.FirstOrDefault(r => string.Equals(r.NewKey, key, StringComparison.Ordinal));
        return copy is null ? key : L.Resource("World3D_CopyLabelFormat", Base3DScene.FriendlyClass(copy.ClassName));
    }

    /// <summary>Staged copies to draw as new boxes at their target places.</summary>
    private List<Base3DCopy> CopiesToDraw()
    {
        if (_basePreview is null) return [];
        return _basePreview.Duplications
            .Where(r => r.After?.Translation is not null)
            .Select(r => new Base3DCopy(r.NewKey, r.ClassName, r.After!, L.Resource("World3D_CopyLabelFormat", Base3DScene.FriendlyClass(r.ClassName)),
                r.ClassPath ?? Session.FindPlacedObject(r.SourceKey)?.ClassPath))
            .ToList();
    }

    /// <summary>Forgets selected keys that no longer exist (after a save, a revert or a deleted copy).</summary>
    private void PruneSelection()
    {
        if (_selected.Count == 0) return;
        var alive = _scene.Objects.Select(o => o.Key).ToHashSet(StringComparer.Ordinal);
        var unresolved = _scene.Unresolved.Select(u => u.Key).ToHashSet(StringComparer.Ordinal);
        _selected.RemoveAll(k => !alive.Contains(k) && !unresolved.Contains(k));
        if (_selectedKey is not null && !_selected.Contains(_selectedKey)) _selectedKey = _selected.LastOrDefault();
    }

    private async Task PickAsync(string? key, bool additive, bool frame)
    {
        if (key is null)
        {
            if (!additive) await SetSelectionAsync([], null, false);
            return;
        }
        ShowInspector();
        if (!additive)
        {
            await SetSelectionAsync([key], key, frame);
            return;
        }
        var keys = _selected.ToList();
        string? primary;
        if (keys.Remove(key)) primary = keys.LastOrDefault();
        else { keys.Add(key); primary = key; }
        await SetSelectionAsync(keys, primary, frame);
    }

    private Task ClickListAsync(string key, MouseEventArgs e) => PickAsync(key, e.CtrlKey || e.ShiftKey || e.MetaKey, frame: !(e.CtrlKey || e.ShiftKey || e.MetaKey));

    private async Task SetSelectionAsync(IEnumerable<string> keys, string? primary, bool frame)
    {
        _levelCopyNote = null;
        _selected.Clear();
        _selected.AddRange(keys.Distinct(StringComparer.Ordinal));
        _selectedKey = primary is not null && _selected.Contains(primary) ? primary : _selected.LastOrDefault();
        if (_grpPivot != PivotCentroid && !_selected.Contains(_grpPivot)) _grpPivot = PivotCentroid;
        LoadNumbers();
        RefreshOpenPanels();
        if (_view is not null)
        {
            await _view.InvokeVoidAsync("setSelection", _selected, _selectedKey);
            if (frame && _selected.Count > 0) await _view.InvokeVoidAsync("frameSelection");
        }
        await SyncGizmoAsync();
    }

    private Task SetPrimaryAsync(string key) => SetSelectionAsync(_selected.ToList(), key, false);

    private Task RemoveFromSelectionAsync(string key)
        => SetSelectionAsync(_selected.Where(k => !string.Equals(k, key, StringComparison.Ordinal)).ToList(), _selectedKey, false);

    private Task ClearSelectionAsync() => SetSelectionAsync([], null, false);

    /// <summary>Selects every object the filters currently show (staged copies are not part of it).</summary>
    private async Task SelectAllShownAsync()
    {
        var keys = _visible.Where(i => i < _scene.Placed.Count).Select(i => _scene.Objects[i].Key).ToList();
        await SetSelectionAsync(keys, keys.FirstOrDefault(), false);
    }

    // ---- group tools --------------------------------------------------------------------------
    private string GroupMessage(PlacedGroupStageResult r)
    {
        var text = L.Resource("World3D_GroupStagedFormat", r.Staged);
        var level = r.Refused.Count(x => x.Reason == PlacedTransformRefusal.LevelPlaced);
        var other = r.NotStaged - level;
        if (level > 0) text += " " + L.Resource("World3D_SkipLevelFormat", level);
        if (other > 0) text += " " + L.Resource("World3D_SkipOtherFormat", other);
        return text;
    }

    private async Task RunGroupAsync(Func<IReadOnlyList<string>, PlacedGroupStageResult> op)
    {
        var result = op(_selected.ToList());
        _groupFeedback = GroupMessage(result);
        await AfterEditAsync(PlacedTransformStageResult.Ok);
    }

    private async Task StageGroupMoveAsync()
    {
        if (!TryNumber(_grpDx, out var dx) || !TryNumber(_grpDy, out var dy) || !TryNumber(_grpDz, out var dz))
        {
            _groupFeedback = L.Resource("World3D_InvalidNumber");
            return;
        }
        await RunGroupAsync(keys => Session.StageGroupMove(keys, dx, dy, dz));
    }

    private async Task StageGroupRotateAsync()
    {
        if (!TryNumber(_grpYaw, out var degrees))
        {
            _groupFeedback = L.Resource("World3D_InvalidNumber");
            return;
        }
        var pivot = PivotFor(_grpPivot, _selected);
        await RunGroupAsync(keys => Session.StageGroupRotate(keys, degrees, pivot));
    }

    private async Task StageGroupSnapAsync()
    {
        double? z = null, yaw = null;
        double zValue = 0, yawValue = 0;
        if (!TryNumber(_snapXY, out var xy) || xy <= 0
            || (!string.IsNullOrWhiteSpace(_snapZ) && (!TryNumber(_snapZ, out zValue) || zValue <= 0))
            || (!string.IsNullOrWhiteSpace(_snapYaw) && (!TryNumber(_snapYaw, out yawValue) || yawValue <= 0)))
        {
            _groupFeedback = L.Resource("World3D_InvalidNumber");
            return;
        }
        if (!string.IsNullOrWhiteSpace(_snapZ)) z = zValue;
        if (!string.IsNullOrWhiteSpace(_snapYaw)) yaw = yawValue;
        await RunGroupAsync(keys => Session.StageGroupSnap(keys, xy, z, yaw));
    }

    private async Task StageGroupAlignAsync()
    {
        if (_selectedKey is not { } reference) return;
        await RunGroupAsync(keys => Session.StageGroupAlignYaw(keys, reference));
    }

    private async Task StageGroupDistributeAsync()
    {
        var axis = Enum.TryParse<PlacementAxis>(_distAxis, out var a) ? a : PlacementAxis.X;
        await RunGroupAsync(keys => Session.StageGroupDistribute(keys, axis));
    }

    private static GroupPivot PivotFor(string choice, IReadOnlyCollection<string> keys)
        => choice != PivotCentroid && keys.Contains(choice) ? GroupPivot.OfObject(choice) : GroupPivot.Centroid;

    // ---- per-edit revert ----------------------------------------------------------------------
    private async Task RevertDeletionAsync(string key)
    {
        Session.RevertPlacedDeletion(key);
        await AfterEditAsync(PlacedTransformStageResult.Ok);
    }

    private async Task RevertDuplicationAsync(int id)
    {
        Session.RevertPlacedDuplication(id);
        await AfterEditAsync(PlacedTransformStageResult.Ok);
    }

    // ---- confirmation panels ------------------------------------------------------------------
    private void CloseOperationPanels()
    {
        _deleteOpen = false;
        _dupOpen = false;
        _deletePreview = null;
        _dupPreview = null;
    }

    private void RefreshOpenPanels()
    {
        if (_deleteOpen && !_deleteBusy) RecomputeDelete();
        if (_dupOpen) RecomputeDuplicate();
    }

    private async Task OpenDeleteAsync()
    {
        _dupOpen = false;
        _dupPreview = null;
        _deleteOpen = true;
        _deletePolicy = DeletePolicy.Default;
        _deletePreview = null;
        _deleteBusy = !Session.OtherSavesLoaded;
        StateHasChanged();
        if (_deleteBusy)
        {
            try { await Session.LoadOtherSavesAsync(); }
            finally { _deleteBusy = false; }
        }
        RecomputeDelete();
    }

    private void RecomputeDelete()
    {
        var (accepted, _) = Session.SplitEditableSelection(_selected, needsTransform: false);
        _deleteKeys = accepted;
        _deletePreview = accepted.Count == 0
            ? null
            : Session.PreviewHypothetical(edits => edits.StageDelete(accepted, _deletePolicy));
    }

    private async Task ConfirmDeleteAsync()
    {
        var result = Session.StagePlacedDelete(_deleteKeys, _deletePolicy);
        _groupFeedback = GroupMessage(result);
        CloseOperationPanels();
        await AfterEditAsync(PlacedTransformStageResult.Ok);
    }

    private IEnumerable<PolicyRow> DeletePolicyRows()
    {
        var p = _deletePolicy;
        void Change(Func<DeletePolicy, DeletePolicy> f) { _deletePolicy = f(_deletePolicy); RecomputeDelete(); }
        yield return new("policy-owned-sockets", "World3D_PolicyOwnedSockets", "World3D_PolicyHelpOwnedSockets", p.OwnedSocketRecords,
            [ReferencePolicy.Drop, ReferencePolicy.Keep, ReferencePolicy.Refuse], v => Change(d => d with { OwnedSocketRecords = v }));
        yield return new("policy-inbound-plugs", "World3D_PolicyInboundPlugs", "World3D_PolicyHelpInboundPlugs", p.InboundPlugs,
            [ReferencePolicy.Refuse, ReferencePolicy.Drop, ReferencePolicy.Keep], v => Change(d => d with { InboundPlugs = v }));
        yield return new("policy-other-refs", "World3D_PolicyOtherRefs", "World3D_PolicyHelpOtherRefs", p.OtherReferences,
            [ReferencePolicy.Refuse, ReferencePolicy.Keep], v => Change(d => d with { OtherReferences = v }));
        yield return new("policy-bed-claims", "World3D_PolicyBedClaims", "World3D_PolicyHelpBedClaims", p.BedClaims,
            [ReferencePolicy.Refuse, ReferencePolicy.Drop], v => Change(d => d with { BedClaims = v }));
        yield return new("policy-teleporter-peers", "World3D_PolicyTeleporterPeers", "World3D_PolicyHelpTeleporterPeers", p.TeleporterPeers,
            [ReferencePolicy.Refuse, ReferencePolicy.Drop, ReferencePolicy.Keep], v => Change(d => d with { TeleporterPeers = v }));
        yield return new("policy-shared-inventories", "World3D_PolicySharedInventories", "World3D_PolicyHelpSharedInventories", p.SharedInventories,
            [ReferencePolicy.Keep, ReferencePolicy.Drop, ReferencePolicy.Refuse], v => Change(d => d with { SharedInventories = v }));
    }

    private IEnumerable<PolicyRow> DuplicatePolicyRows()
    {
        yield return new("policy-external-power", "World3D_PolicyExternalPower", "World3D_PolicyHelpExternalPower", _dupPower,
            [ReferencePolicy.Drop, ReferencePolicy.Keep, ReferencePolicy.Refuse], v => { _dupPower = v; RecomputeDuplicate(); });
        yield return new("policy-external-teleporters", "World3D_PolicyExternalTeleporters", "World3D_PolicyHelpExternalTeleporters", _dupTeleporters,
            [ReferencePolicy.Drop, ReferencePolicy.Keep, ReferencePolicy.Refuse], v => { _dupTeleporters = v; RecomputeDuplicate(); });
    }

    private void OnAxisChanged(ChangeEventArgs e) => _distAxis = e.Value?.ToString() ?? "X";
    private void OnGroupPivotChanged(ChangeEventArgs e) => _grpPivot = e.Value?.ToString() ?? PivotCentroid;
    private void OnDupXChanged(ChangeEventArgs e) { _dupX = e.Value?.ToString() ?? string.Empty; RecomputeDuplicate(); }
    private void OnDupYChanged(ChangeEventArgs e) { _dupY = e.Value?.ToString() ?? string.Empty; RecomputeDuplicate(); }
    private void OnDupZChanged(ChangeEventArgs e) { _dupZ = e.Value?.ToString() ?? string.Empty; RecomputeDuplicate(); }
    private void OnDupYawChanged(ChangeEventArgs e) { _dupYaw = e.Value?.ToString() ?? string.Empty; RecomputeDuplicate(); }
    private void OnDupPivotChanged(ChangeEventArgs e) { _dupPivot = e.Value?.ToString() ?? PivotCentroid; RecomputeDuplicate(); }
    private void OnDupContentsChanged(ChangeEventArgs e) { _dupCopyContents = e.Value is true; RecomputeDuplicate(); }

    private static ReferencePolicy ParsePolicy(string? text)
        => Enum.TryParse<ReferencePolicy>(text, out var value) ? value : ReferencePolicy.Refuse;

    private void OpenDuplicate()
    {
        _deleteOpen = false;
        _deletePreview = null;
        _dupOpen = true;
        _dupCopyContents = false;
        _dupPower = DuplicatePolicy.Default.ExternalPowerLinks;
        _dupTeleporters = DuplicatePolicy.Default.ExternalTeleporterPeers;
        _dupYaw = "0";
        _dupPivot = PivotCentroid;
        var (accepted, _) = Session.SplitEditableSelection(_selected, needsTransform: true);
        _dupKeys = accepted;
        // A sensible first step: clear the selection's own width along X plus a metre, on a 50 cm grid, never under 2 m.
        var xs = accepted.Select(k => Session.CurrentPlacedTransform(k)?.Translation?.X).Where(x => x.HasValue).Select(x => x!.Value).ToList();
        var extent = xs.Count > 1 ? xs.Max() - xs.Min() : 0;
        var step = Math.Max(200, Math.Ceiling((extent + 100) / 50) * 50);
        _dupX = step.ToString("0.##", CultureInfo.InvariantCulture);
        _dupY = "0";
        _dupZ = "0";
        RecomputeDuplicate();
    }

    private void RecomputeDuplicate()
    {
        var (accepted, _) = Session.SplitEditableSelection(_selected, needsTransform: true);
        _dupKeys = accepted;
        if (_dupPivot != PivotCentroid && !accepted.Contains(_dupPivot)) _dupPivot = PivotCentroid;
        _dupPreview = null;
        _dupError = null;
        if (!TryNumber(_dupX, out var x) || !TryNumber(_dupY, out var y) || !TryNumber(_dupZ, out var z) || !TryNumber(_dupYaw, out var yaw))
        {
            _dupError = L.Resource("World3D_InvalidNumber");
            return;
        }
        if (accepted.Count == 0) return;
        var offset = new PlacedVector(x, y, z);
        var pivot = PivotFor(_dupPivot, accepted);
        var policy = CurrentDuplicatePolicy();
        var id = -1;
        _dupPreview = Session.PreviewHypothetical(edits => id = edits.StageDuplicate(accepted, offset, yaw, pivot, policy).Id);
        _dupPreviewId = id;
    }

    private DuplicatePolicy CurrentDuplicatePolicy() => new()
    {
        Contents = _dupCopyContents ? ContentsMode.Copy : ContentsMode.Empty,
        ExternalPowerLinks = _dupPower,
        ExternalTeleporterPeers = _dupTeleporters,
    };

    private async Task ConfirmDuplicateAsync()
    {
        if (!TryNumber(_dupX, out var x) || !TryNumber(_dupY, out var y) || !TryNumber(_dupZ, out var z) || !TryNumber(_dupYaw, out var yaw)) return;
        var result = Session.StagePlacedDuplicate(_dupKeys, new PlacedVector(x, y, z), yaw, PivotFor(_dupPivot, _dupKeys), CurrentDuplicatePolicy());
        _groupFeedback = GroupMessage(result);
        CloseOperationPanels();
        await AfterEditAsync(PlacedTransformStageResult.Ok);
    }
}
