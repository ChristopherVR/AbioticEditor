using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>
/// The live counterpart to the file session's effects slice, plus the live-only survival
/// toggles. Every change goes straight to the running game and is followed by a fresh read, so
/// the tab shows what the game actually kept.
/// </summary>
public sealed class LivePlayerConditionsSession : IPlayerEffectsSession, IPlayerTogglesSession
{
    // EBodyLimbs, in enum order (CheatConsoleCommands' AFUtils Enums.lua).
    private static readonly string[] LimbNames =
        ["AllBones", "RandomBone", "RandomBoneExceptHead", "Head", "Torso", "LeftArm", "RightArm", "LeftLeg", "RightLeg"];

    private readonly LivePlayerConditionsChannel _channel;
    private string? _playerId;
    private LiveEffectsState _effects = new([], null, false, false);

    private LivePlayerConditionsSession(LivePlayerConditionsChannel channel, string? playerId)
    {
        _channel = channel;
        _playerId = playerId;
    }

    /// <summary>Reads the effects and toggles of <paramref name="playerId"/> (or the local player).</summary>
    public static async Task<LivePlayerConditionsSession> ConnectAsync(
        LivePlayerConditionsChannel channel, string? playerId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var session = new LivePlayerConditionsSession(channel, playerId);
        await session.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return session;
    }

    public IReadOnlyList<PlayerEffectEdit> Effects => _effects.Effects
        .Select(effect => new PlayerEffectEdit(effect.Row, LimbLabel(effect.Limb), effect.ExpireTime < 0, _effects.SecondsLeft(effect))
            { Source = effect })
        .ToList();

    public bool AppliesImmediately => true;
    public bool CanEditEffects => _effects.IsHost;
    public bool CanAddEffects => _effects.IsHost;
    public bool OnlySavedEffects => false;
    public bool SupportsDebuffBlocking => true;
    public bool BlockNewDebuffs => _effects.BlockDebuffs;

    public IReadOnlyDictionary<string, bool> Toggles { get; private set; } = new Dictionary<string, bool>();
    public bool CanEditToggles => _effects.IsHost;

    public async Task AddEffectAsync(BuffInfo effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        await _channel.AddEffectAsync(effect.Row, effect.NoExpiration ? null : effect.DefaultDuration > 0 ? effect.DefaultDuration : null,
            _playerId).ConfigureAwait(false);
        await RefreshEffectsAsync().ConfigureAwait(false);
    }

    public async Task RemoveEffectAsync(PlayerEffectEdit effect)
    {
        if (effect?.Source is not LiveEffect live) return;
        await _channel.RemoveEffectAsync(live.Row, live.Limb, _playerId).ConfigureAwait(false);
        await RefreshEffectsAsync().ConfigureAwait(false);
    }

    public async Task ClearDebuffsAsync()
    {
        await _channel.ClearDebuffsAsync(_playerId).ConfigureAwait(false);
        await RefreshEffectsAsync().ConfigureAwait(false);
    }

    public async Task SetBlockNewDebuffsAsync(bool enabled)
    {
        await _channel.SetBlockDebuffsAsync(enabled, _playerId).ConfigureAwait(false);
        await RefreshEffectsAsync().ConfigureAwait(false);
    }

    public async Task SetToggleAsync(string name, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Toggles = await _channel.SetTogglesAsync(new Dictionary<string, bool> { [name] = enabled }, _playerId).ConfigureAwait(false);
    }

    // A genuine zero-arg overload: LiveConnect's periodic refresh finds RefreshAsync by reflection
    // (GetMethod("RefreshAsync", Type.EmptyTypes)), so effects running out show up on their own.
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await RefreshEffectsAsync(cancellationToken).ConfigureAwait(false);
        Toggles = await _channel.GetTogglesAsync(_playerId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SwitchPlayerAsync(string? playerId, CancellationToken cancellationToken = default)
    {
        _playerId = playerId;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshEffectsAsync(CancellationToken cancellationToken = default)
        => _effects = await _channel.GetEffectsAsync(_playerId, cancellationToken).ConfigureAwait(false);

    private static string? LimbLabel(int limb) =>
        limb > 0 && limb < LimbNames.Length ? "EBodyLimbs::" + LimbNames[limb] : null;
}
