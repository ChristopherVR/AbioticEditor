namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>
/// Which region of the running game's world is currently loaded - the live counterpart of picking
/// a <c>WorldSave_&lt;Region&gt;.sav</c> file offline. See <c>world.info</c> in
/// <c>live-agent/AbioticEditorLiveAgentLua/Scripts/main.lua</c>: it reuses the exact same evidenced
/// read <c>spawn.get</c> already uses for its own <c>levelName</c> field (the local controller's
/// own <c>ActiveLevelName</c>, a display-only streaming level name - NOT the file's
/// <c>RespawnLevelGuid</c>) rather than introducing a new, unverified
/// <c>UEHelpers.GetWorld():GetMapName()</c> call. Reading this needs no host authority: it is not
/// a write, so a joined client reads its own accurate value too.
/// </summary>
public sealed class LiveWorldInfoChannel(ILiveGameChannel channel)
{
    private readonly ILiveGameChannel _channel = channel ?? throw new ArgumentNullException(nameof(channel));

    public async Task<LiveWorldInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        var wire = await _channel.RequestAsync<InfoWire>("world.info", payload: null, cancellationToken)
            .ConfigureAwait(false);
        return new LiveWorldInfo(wire.LevelToken, wire.IsHost);
    }

    private sealed record InfoWire(string? LevelToken, bool IsHost);
}

/// <summary>The running game's current streaming level token (e.g. <c>Facility_MFWest</c>), as
/// read by <see cref="LiveWorldInfoChannel.GetAsync"/>. Null when no local controller is loaded
/// yet (main menu, or between loading screens). Run it through
/// <c>AbioticEditor.Core.WorldSaves.WorldAreaCatalog</c> for a friendly region name, or match
/// <c>"WorldSave_" + LevelToken + ".sav"</c> against a world folder's own save files to find which
/// region save the player is standing in right now (see
/// <see cref="AbioticEditor.Core.WorldSaves.LiveWorldFolderLocator"/> for finding that folder in
/// the first place).</summary>
public sealed record LiveWorldInfo(string? LevelToken, bool IsHost);
