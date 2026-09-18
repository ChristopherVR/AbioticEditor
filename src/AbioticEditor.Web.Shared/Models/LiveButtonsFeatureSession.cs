using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.World;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live-edit counterpart to <see cref="WorldSaveSession"/>'s world-map features browser for
/// <see cref="ButtonsFeatureId"/> ("Buttons", <see cref="ButtonMapFeature"/>'s live twin) -
/// implements the same <see cref="IWorldFeaturesSession"/> boundary <c>WorldFeaturesTab</c>
/// already binds to, the same shape <see cref="LivePortalsFeatureSession"/> uses for the one
/// other feature with a live equivalent.
///
/// <para>Confirmed against the coordinator's CUE4Parse class dump and Button_Generic_C's own
/// blueprint bytecode - see <see cref="LiveButtonsChannel"/> and the Lua module's own header
/// comment (<c>live-agent/AbioticEditorLiveAgentLua/Scripts/areas/buttons.lua</c>) for the full
/// mapping. <c>enabled</c>/<c>activated</c>/<c>noReset</c> are real, independently settable live
/// properties (<c>NOT ButtonDisabled</c>, <c>Activated</c>, <c>NoVignetteReset</c>); a null value
/// only means this particular actor could not be read right now (renders as a read-only "not
/// available live" row rather than a toggle). <c>pressedOnce</c> (round 110) is now also a real
/// toggle: the game's own save path (<c>UpdateButtonSaveData</c>) forces it <c>true</c> as an
/// unconditional side effect of persisting any OTHER field, so this session writes the leaf and
/// persists it through a direct call to the game mode's own <c>UpdateActorToWorldSave</c> instead,
/// bypassing that wrapper entirely - see <see cref="LiveButtonsChannel"/>'s remarks for the honest
/// caveat that a <c>false</c> write is only durable until the button is next really interacted
/// with.</para>
/// </summary>
public sealed class LiveButtonsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>ButtonMapFeature.Id</c> (Core/WorldSaves/Features/ButtonMapFeature.cs).</summary>
    public const string ButtonsFeatureId = "buttons";

    private readonly LiveButtonsChannel _channel;

    private LiveButtonsFeatureSession(LiveButtonsChannel channel, LiveButtonDirectory directory)
    {
        _channel = channel;
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though buttons.lua already keys every row by the actor's own unique full name.
        Buttons = LiveFeatureRows.DistinctById(directory.Buttons, b => b.Id);
        IsHost = directory.IsHost;
    }

    public static async Task<LiveButtonsFeatureSession> ConnectAsync(
        LiveButtonsChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var directory = await channel.GetAsync(cancellationToken).ConfigureAwait(false);
        return new LiveButtonsFeatureSession(channel, directory);
    }

    public IReadOnlyList<LiveButton> Buttons { get; private set; }
    public bool IsHost { get; private set; }

    /// <summary>Always false: a toggle already reached the running game by the time it returns,
    /// so there is never a client-side staged copy - see <see cref="LivePortalsFeatureSession.IsDirty"/>.</summary>
    public bool IsDirty => false;

    /// <summary>Raised after <see cref="RefreshAsync"/> re-reads the world and after every
    /// mutation (each of which already ends by refreshing).</summary>
    public event Action? Changed;

    string IWorldFeaturesSession.Path => string.Empty;
    IReadOnlyList<WorldDeployable> IWorldFeaturesSession.Deployables => [];

    // A genuine zero-arg overload: LiveConnect's periodic refresh loop finds this by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)) - see LivePortalsFeatureSession's identical
    // remark, the same reason this exists there.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var directory = await _channel.GetAsync(cancellationToken).ConfigureAwait(false);
        // Defensive dedupe (round 122): see LiveFeatureRows's own remarks for why this exists
        // even though buttons.lua already keys every row by the actor's own unique full name.
        Buttons = LiveFeatureRows.DistinctById(directory.Buttons, b => b.Id);
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField BoolOrUnavailable(string id, string label, bool? value, string hint)
        => value is { } known
            ? WorldMapField.Bool(id, label, known, hint: hint + " Applies live immediately.")
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this property off this button actor right now (it may have just "
                    + "unloaded). Try again after the next refresh, or edit it in the save file instead.");

    /// <summary>Round 110: a real toggle now, not just a display. Renders read-only only when this
    /// specific actor could not be read right now (see the class remarks) - a known value is always
    /// editable, in either direction.</summary>
    private static WorldMapField PressedOnceField(bool? value)
        => value is { } known
            ? WorldMapField.Bool("pressedOnce", "Pressed once", known,
                hint: "True once the button has been triggered at least once. Applies live immediately by "
                    + "writing the save data directly (the game's own persistence path always sets this true "
                    + "as a side effect of saving any OTHER field on this button, so setting it back to false "
                    + "here only lasts until this button is next actually interacted with in-game).")
            : WorldMapField.ReadOnly("pressedOnce", "Pressed once", "not available live",
                hint: "Could not read this property off this button actor right now (it may have just "
                    + "unloaded). Try again after the next refresh, or edit it in the save file instead.");

    public WorldMapFeatureSnapshot? MapFeature(string featureId)
    {
        if (!string.Equals(featureId, ButtonsFeatureId, StringComparison.Ordinal)) return null;
        var entries = Buttons.Select(b => new WorldMapEntry(
            b.Id,
            b.Label,
            new[]
            {
                BoolOrUnavailable("enabled", "Enabled", b.Enabled, "true = the button can currently be interacted with."),
                BoolOrUnavailable("activated", "Activated", b.Activated, "true = the button is in an activated/latched state."),
                PressedOnceField(b.PressedOnce),
                BoolOrUnavailable("noReset", "No reset", b.NoReset,
                    "true = the button keeps its current state and is skipped by the next reset cycle."),
            })).ToArray();
        return new WorldMapFeatureSnapshot(
            ButtonsFeatureId, "Buttons",
            "Interactive world buttons: toggle whether each is enabled or activated, whether it skips the "
                + "next reset, and whether it has been pressed once (a \"false\" write here only lasts "
                + "until the button is next actually interacted with in-game).",
            MapName: "ButtonMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, ButtonsFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        if (!WorldMapAccessor.TryParseBool(value, out var wanted))
        {
            return WorldEditResult.Failure($"'{value}' is not a boolean (use true/false).");
        }

        var current = Buttons.FirstOrDefault(b => string.Equals(b.Id, entryKey, StringComparison.Ordinal));
        if (current is null) return WorldEditResult.Failure("button not found (it may have been unloaded).");

        // A null current value means "could not read this property off this actor right now" (see
        // the class remarks), not "false" - it never matches wanted, so the edit is still
        // attempted below rather than silently reported as NoChange.
        (bool? CurrentValue, LiveButtonEdit? Edit) resolved = fieldId switch
        {
            "enabled" => (current.Enabled, new LiveButtonEdit(entryKey, Enabled: wanted)),
            "activated" => (current.Activated, new LiveButtonEdit(entryKey, Activated: wanted)),
            "noReset" => (current.NoReset, new LiveButtonEdit(entryKey, NoReset: wanted)),
            // Round 110: genuinely settable, in either direction - see the class remarks. A null
            // current value never matches wanted, so the edit is still attempted (same as the
            // other three fields), and a false write is honestly transient (see the hint text).
            "pressedOnce" => (current.PressedOnce, new LiveButtonEdit(entryKey, PressedOnce: wanted)),
            _ => (null, null),
        };
        if (resolved.Edit is not { } edit) return WorldEditResult.Failure($"'{fieldId}' cannot be changed live.");
        if (resolved.CurrentValue == wanted) return WorldEditResult.NoChange;

        try
        {
            await _channel.SetAsync([edit]).ConfigureAwait(false);
        }
        catch (LiveAgentException ex)
        {
            // The Lua side reports a named, player-safe error when it could not find a live
            // property for this field (see buttons.lua's own header comment) - surface it as-is
            // rather than a generic failure.
            return WorldEditResult.Failure(ex.Message);
        }
        await RefreshAsync().ConfigureAwait(false);
        return WorldEditResult.Success;
    }

    public Task<WorldEditResult> RemoveMapFeatureEntry(string featureId, string entryKey)
        => Task.FromResult(WorldEditResult.Failure("world buttons cannot be removed."));
}
