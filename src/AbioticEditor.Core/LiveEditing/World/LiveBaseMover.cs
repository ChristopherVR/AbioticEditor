using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.LiveEditing.World;

/// <summary>The running world's save on disk, right after the game saved it.</summary>
/// <param name="WorldName">The world's folder name (the game's <c>ActiveWorldSaveName</c>).</param>
/// <param name="Folder">The world folder.</param>
/// <param name="File">The area save the pieces live in.</param>
/// <param name="Data">That save, read fresh.</param>
public sealed record RunningWorldSave(string WorldName, string Folder, string File, WorldSaveData Data);

/// <summary>Pieces of the running world lifted into a package, with the live piece behind each saved key.</summary>
public sealed record LiveLift(RunningWorldSave World, BasePackage Package, IReadOnlyDictionary<string, string> LiveIdByKey,
    IReadOnlyList<string> NotSaved);

/// <summary>What bringing a package into the running world did.</summary>
/// <param name="Placed">The new live pieces, by the package key they came from.</param>
/// <param name="Notes">Plain-language notes about details the running game could not take.</param>
public sealed record LiveBringResult(IReadOnlyDictionary<string, string> Placed, IReadOnlyList<string> Notes, int ContentsNotRefilled = 0);

/// <summary>
/// Moving bases with a running game (live editing), the same moves the offline editor makes:
/// <list type="bullet">
/// <item>Out of the running world (to another world, or a base file): the game is asked to save
/// (the same native save action its own player controller uses), the pieces' exact records are
/// read from the fresh save, and only after the receiving world is written are the live pieces
/// removed (and the world saved again), so a failure never loses a base.</item>
/// <item>Within the running world: the live pieces themselves move, so everything stays (contents,
/// wiring, names).</item>
/// <item>Into the running world: each piece is placed live (from its kind), then given back what it
/// held, its name, paint and bench upgrades. What a fresh piece cannot be given live is listed in
/// <see cref="LiveBringResult.Notes"/>.</item>
/// </list>
/// </summary>
public sealed class LiveBaseMover
{
    private readonly ILiveGameChannel _channel;
    private readonly LiveBasesChannel _bases;
    private readonly LiveContainersChannel _containers;
    private readonly LivePowerSocketsChannel _power;
    private readonly Func<IReadOnlyList<DiscoveredWorld>> _discover;

    public LiveBaseMover(ILiveGameChannel channel, Func<IReadOnlyList<DiscoveredWorld>>? discover = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _bases = new LiveBasesChannel(channel);
        _containers = new LiveContainersChannel(channel);
        _power = new LivePowerSocketsChannel(channel);
        _discover = discover ?? SaveDiscovery.DiscoverAll;
    }

    /// <summary>How long a save may take before the editor gives up waiting.</summary>
    public TimeSpan SaveTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the save's progress is asked for.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    private sealed record SaveWire(bool Started, bool Saving, string? WorldName);
    private sealed record SavingWire(bool Saving, string? WorldName);

    /// <summary>Asks the game to save the world now and waits until it has. Returns the world's folder name.</summary>
    public async Task<string> SaveWorldAsync(CancellationToken cancellationToken = default)
    {
        var started = await _channel.RequestAsync<SaveWire>("world.save", null, cancellationToken).ConfigureAwait(false);
        var name = started.WorldName;
        var deadline = DateTime.UtcNow + SaveTimeout;
        // The save runs on the game's own save thread; give it a moment to raise its flag, then wait for it to drop.
        await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var state = await _channel.RequestAsync<SavingWire>("world.saving", null, cancellationToken).ConfigureAwait(false);
            name ??= state.WorldName;
            if (!state.Saving) break;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The game is still saving. Try again in a moment.");
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
        return name ?? throw new InvalidOperationException("The game did not say which world is running.");
    }

    /// <summary>
    /// The running world's area save on disk: the world folder of that name whose area save was
    /// written most recently (a world of the same name can exist for another account).
    /// </summary>
    public RunningWorldSave FindRunningWorld(string worldName, string areaFile = "WorldSave_Facility.sav")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        var candidates = _discover()
            .Where(w => string.Equals(w.WorldName, worldName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(w.FolderPath), worldName, StringComparison.OrdinalIgnoreCase))
            .Select(w => Path.Combine(w.FolderPath, areaFile))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
        if (candidates.Count == 0) throw new FileNotFoundException($"The running world \"{worldName}\" was not found on this computer.");
        var file = candidates[0];
        return new RunningWorldSave(worldName, Path.GetDirectoryName(file)!, file, WorldSaveReader.ReadFromFile(file));
    }

    /// <summary>Saves the world and reads the running world's area save fresh.</summary>
    public async Task<RunningWorldSave> SaveAndReadAsync(string areaFile = "WorldSave_Facility.sav", CancellationToken cancellationToken = default)
        => FindRunningWorld(await SaveWorldAsync(cancellationToken).ConfigureAwait(false), areaFile);

    /// <summary>
    /// Lifts live pieces into a package: the game saves, and the pieces' records are read from the
    /// fresh save by their save keys. Pieces the save does not hold (never saved yet) are listed in
    /// <see cref="LiveLift.NotSaved"/>.
    /// </summary>
    public async Task<LiveLift> LiftAsync(IReadOnlyList<LiveDeployable> pieces, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        var area = pieces.Select(p => p.Region).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r)) is { } region
            && !region.StartsWith("Facility_", StringComparison.OrdinalIgnoreCase) ? $"WorldSave_{region}.sav" : "WorldSave_Facility.sav";
        var world = await SaveAndReadAsync(area, cancellationToken).ConfigureAwait(false);
        var byKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notSaved = new List<string>();
        foreach (var p in pieces)
        {
            if (p.AssetId is { Length: 32 } key && WorldMapAccessor.FindEntry(world.Data.Raw, "DeployedObjectMap", key) is not null) byKey[key] = p.Id;
            else notSaved.Add(p.Id);
        }
        if (byKey.Count == 0) throw new InvalidOperationException("None of these pieces is in the world's save yet.");
        var package = BasePackage.Create(world.Data, byKey.Keys, world.WorldName, Path.GetFileName(world.File), out _);
        var liveByKey = byKey.Where(kv => package.Pieces.Any(p => string.Equals(p.Key, kv.Key, StringComparison.OrdinalIgnoreCase)))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        return new LiveLift(world, package, liveByKey, notSaved);
    }

    /// <summary>Where each piece of <paramref name="package"/> lands for a shift and turn (and optional per-piece heights).</summary>
    public static IReadOnlyDictionary<string, PlacedObjectTransform> Placements(
        BasePackage package, PlacedVector offset, double yawDegrees, IReadOnlyDictionary<string, double>? heights = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        var pivot = package.Centre ?? new PlacedVector(0, 0, 0);
        var result = new Dictionary<string, PlacedObjectTransform>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in WorldMapAccessor.Entries(package.Donor.Raw, "DeployedObjectMap"))
        {
            if (PlacedObjectCensus.ReadTransform(e.Props) is not { Translation: { } t } transform) continue;
            var moved = yawDegrees == 0 ? t : PlacementMath.RotateAboutZ(t, pivot, yawDegrees);
            var at = new PlacedVector(moved.X + offset.X, moved.Y + offset.Y, moved.Z + offset.Z);
            if (heights?.TryGetValue(e.Key, out var z) == true) at = at with { Z = z };
            var rotation = transform.EffectiveRotation;
            if (yawDegrees != 0) rotation = PlacementMath.ComposeYaw(rotation, yawDegrees);
            result[e.Key] = transform with { Translation = at, Rotation = rotation };
        }
        return result;
    }

    /// <summary>
    /// Moves lifted pieces within the running world: each live piece is moved to its new place, so it
    /// keeps everything it has. The world is saved afterwards.
    /// </summary>
    public async Task MoveWithinAsync(LiveLift lift, PlacedVector offset, double yawDegrees,
        IReadOnlyDictionary<string, double>? heights = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lift);
        foreach (var (key, place) in Placements(lift.Package, offset, yawDegrees, heights))
        {
            if (!lift.LiveIdByKey.TryGetValue(key, out var liveId) || place.Translation is not { } at) continue;
            await _bases.MoveAsync(liveId, at.X, at.Y, at.Z, place.EffectiveRotation.YawDegrees, cancellationToken).ConfigureAwait(false);
        }
        await SaveWorldAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes live pieces (with what they hold) and saves the world.</summary>
    public async Task<int> RemoveAsync(IReadOnlyList<string> liveIds, CancellationToken cancellationToken = default)
    {
        var removed = await _bases.RemoveAsync(liveIds, cancellationToken).ConfigureAwait(false);
        await SaveWorldAsync(cancellationToken).ConfigureAwait(false);
        return removed;
    }

    /// <summary>
    /// Places every piece of <paramref name="package"/> in the running world and gives each back what it
    /// held, its name, paint and bench upgrades. All or nothing: when a piece cannot be placed, the ones
    /// already placed are removed again and the error is thrown. The world is saved afterwards.
    /// </summary>
    /// <param name="donors">Live pieces already in the world, by class path, used when a kind cannot be loaded by its class.</param>
    /// <param name="withContents">False places the pieces empty (a copy of pieces that stay where they are, so nothing is duplicated).</param>
    public async Task<LiveBringResult> BringAsync(BasePackage package, PlacedVector offset, double yawDegrees,
        IReadOnlyDictionary<string, double>? heights, IReadOnlyDictionary<string, string>? donors,
        IProgress<(int Done, int Total)>? progress = null, bool withContents = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        var places = Placements(package, offset, yawDegrees, heights);
        var data = package.Donor;
        var deployables = data.Deployables.ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);
        var containers = data.Containers.Where(c => c.Source == WorldContainerSource.Deployed)
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var placed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notRefilled = 0;
        var notes = new List<string>();
        try
        {
            var done = 0;
            foreach (var piece in package.Pieces)
            {
                if (!places.TryGetValue(piece.Key, out var place) || place.Translation is not { } at) continue;
                var r = place.EffectiveRotation;
                string liveId;
                var newKey = Guid.NewGuid().ToString("N").ToUpperInvariant();
                try
                {
                    liveId = await _bases.SpawnAsync(null, piece.ClassPath, newKey,
                        at.X, at.Y, at.Z, r.PitchDegrees, r.YawDegrees, r.RollDegrees, cancellationToken).ConfigureAwait(false);
                }
                catch (LiveAgentException) when (piece.ClassPath is { } cls && donors?.TryGetValue(cls, out var donor) == true)
                {
                    liveId = await _bases.SpawnAsync(donor, null, newKey,
                        at.X, at.Y, at.Z, r.PitchDegrees, r.YawDegrees, r.RollDegrees, cancellationToken).ConfigureAwait(false);
                }
                placed[piece.Key] = liveId;
                newKeys[piece.Key] = newKey;

                if (withContents && containers.TryGetValue(piece.Key, out var held) && held.Inventories.Count > 0)
                {
                    var edits = held.Inventories[0].Slots.Where(s => !s.IsEmpty)
                        .Select(s => new LiveContainerSlotEdit(s.Index, ItemId: s.ItemId, Stack: s.Count, Durability: s.Durability,
                            MaxDurability: s.MaxDurability, AmmoInMagazine: s.AmmoInMagazine, Details: LiveItemDetails.FromSlot(s)))
                        .ToList();
                    if (edits.Count > 0)
                    {
                        try { await _containers.SetAsync(liveId, edits, cancellationToken).ConfigureAwait(false); }
                        catch (LiveAgentException)
                        {
                            notRefilled++;
                            notes.Add($"{Label(piece)}: what it held could not be put back live ({edits.Count} slot(s)).");
                        }
                    }
                    if (held.Inventories.Count > 1) notes.Add($"{Label(piece)}: only its main storage was refilled live.");
                }
                if (deployables.TryGetValue(piece.Key, out var d))
                {
                    if (!string.IsNullOrWhiteSpace(piece.CustomName)) await _bases.SetCustomNameAsync(liveId, piece.CustomName, cancellationToken).ConfigureAwait(false);
                    if (d.IsPainted && d.PaintColorValue is { } paint) await TryAsync(() => _bases.SetPaintColorAsync(liveId, paint, cancellationToken), notes, $"{Label(piece)}: its paint could not be set live.").ConfigureAwait(false);
                    foreach (var upgrade in d.InstalledUpgrades)
                    {
                        await TryAsync(() => _bases.SetBenchUpgradeAsync(liveId, upgrade, true, cancellationToken), notes, $"{Label(piece)}: the {upgrade} upgrade could not be fitted live.").ConfigureAwait(false);
                    }
                }
                progress?.Report((++done, package.Pieces.Count));
            }
        }
        catch
        {
            if (placed.Count > 0)
            {
                try { await _bases.RemoveAsync(placed.Values.ToList(), CancellationToken.None).ConfigureAwait(false); }
                catch (Exception ex) when (ex is LiveAgentException or IOException or InvalidOperationException or TimeoutException) { }
            }
            throw;
        }

        // Cables between the moved pieces: each outlet of a new piece (its new key plus the outlet's
        // digit) is plugged into the new piece that was plugged into it before. Links to anything
        // left behind are not made (as offline).
        var unplugged = 0;
        foreach (var socket in PlacedPowerRecords.Read(data))
        {
            if (socket.OwnerKey is not { } owner || socket.Plugged is not { } device) continue;
            if (!newKeys.TryGetValue(owner, out var ownerKey) || !placed.TryGetValue(device, out var deviceLive)) continue;
            try
            {
                await _power.PlugAsync(null, ownerKey + socket.Id[owner.Length..], deviceLive, true, cancellationToken).ConfigureAwait(false);
            }
            catch (LiveAgentException)
            {
                unplugged++;
            }
        }
        if (unplugged > 0) notes.Add($"{unplugged} power cable(s) between the pieces could not be reconnected live: plug them in again in the game.");
        if (data.Deployables.Any(d => d.ClassName?.Contains("TeleporterPad", StringComparison.OrdinalIgnoreCase) == true))
            notes.Add("Teleporter pads arrive without a pairing: pair them in the game.");
        if (data.Deployables.Any(d => d.HasClaimMarker && d.OwnerId is not null))
            notes.Add("Claimed beds arrive unclaimed.");
        await SaveWorldAsync(cancellationToken).ConfigureAwait(false);
        return new LiveBringResult(placed, notes.Distinct(StringComparer.Ordinal).ToList(), notRefilled);
    }

    private static string Label(BasePackagePiece piece) => piece.CustomName ?? piece.ClassName ?? piece.Key;

    private static async Task TryAsync(Func<Task> action, List<string> notes, string note)
    {
        try { await action().ConfigureAwait(false); }
        catch (LiveAgentException) { notes.Add(note); }
    }
}
