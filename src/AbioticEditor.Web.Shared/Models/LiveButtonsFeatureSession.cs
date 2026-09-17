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
/// available live" row rather than a toggle). <c>pressedOnce</c> is different: it is readable
/// (<c>ButtonSaveData.ButtonHasBeenPressedOnce_...</c>) but genuinely NOT independently settable -
/// the game's own save path forces it <c>true</c> as an unconditional side effect of persisting
/// any other field, with no live path found that clears it back to <c>false</c> - so it always
/// renders read-only here, never a toggle, regardless of whether its value is known.</para>
/// </summary>
public sealed class LiveButtonsFeatureSession : IWorldFeaturesSession
{
    /// <summary>Matches <c>ButtonMapFeature.Id</c> (Core/WorldSaves/Features/ButtonMapFeature.cs).</summary>
    public const string ButtonsFeatureId = "buttons";

    private readonly LiveButtonsChannel _channel;

    private LiveButtonsFeatureSession(LiveButtonsChannel channel, LiveButtonDirectory directory)
    {
        _channel = channel;
        Buttons = directory.Buttons;
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
        Buttons = directory.Buttons;
        IsHost = directory.IsHost;
        Changed?.Invoke();
    }

    private static WorldMapField BoolOrUnavailable(string id, string label, bool? value, string hint)
        => value is { } known
            ? WorldMapField.Bool(id, label, known, hint: hint + " Applies live immediately.")
            : WorldMapField.ReadOnly(id, label, "not available live",
                hint: "Could not read this property off this button actor right now (it may have just "
                    + "unloaded). Try again after the next refresh, or edit it in the save file instead.");

    /// <summary>"Pressed once" always renders read-only, whether or not its value is known - see
    /// the class remarks for why there is no live write path for it at all.</summary>
    private static WorldMapField PressedOnceField(bool? value)
        => WorldMapField.ReadOnly("pressedOnce", "Pressed once",
            value is { } known ? (known ? "true" : "false") : "not available live",
            hint: "True once the button has been triggered at least once. The game always sets this the "
                + "moment ANY other field on this button is saved, live or offline, and no live path was "
                + "found that clears it back to false - edit the save file directly for that.");

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
            "Interactive world buttons: toggle whether each is enabled or activated, and whether it skips the "
                + "next reset. \"Pressed once\" is shown for reference only - the game itself decides it.",
            MapName: "ButtonMap", SupportsRemoval: false, RemoveActionLabel: string.Empty, entries);
    }

    public async Task<WorldEditResult> SetMapFeatureField(string featureId, string entryKey, string fieldId, string? value)
    {
        if (!string.Equals(featureId, ButtonsFeatureId, StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("this feature has no live equivalent.");
        }
        // Rejected before even parsing the value: no live path sets this independently, ever -
        // see the class remarks and buttons.lua's own header comment. A round trip to the game
        // would only come back with the exact same refusal.
        if (string.Equals(fieldId, "pressedOnce", StringComparison.Ordinal))
        {
            return WorldEditResult.Failure("'pressed once' cannot be set independently live - the game "
                + "always marks it true the moment any other field on the same button is saved, and no "
                + "live path was found that clears it back to false.");
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
