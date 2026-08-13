namespace AbioticEditor.Core.GamePass;

/// <summary>What a running process has to do with a Game Pass save.</summary>
public enum GamePassProcessRole
{
    /// <summary>Abiotic Factor itself. While the title runs it owns its Connected Storage
    /// containers, and Microsoft's own guidance is that writing save data underneath a running
    /// title can leave its saves in an indeterminate state.</summary>
    Game,

    /// <summary>The Xbox app. It can start a sync of its own, but it also sits open on most Game
    /// Pass machines all day, so its presence is worth saying out loud and nothing more.</summary>
    XboxApp,

    /// <summary>Gaming Services or the Connected Storage sync worker - the part that actually moves
    /// containers between this machine and the cloud.</summary>
    SyncService,
}

/// <summary>One process found running that matters to a Game Pass save.</summary>
/// <param name="Name">The process name as Windows reports it (no <c>.exe</c>).</param>
/// <param name="Role">What that process is to a save.</param>
public sealed record GamePassRunningProcess(string Name, GamePassProcessRole Role);

/// <summary>
/// The result of looking for processes that could be writing a Game Pass save behind the editor's
/// back. Deliberately not a bare bool: "nothing found" and "could not look" are different answers,
/// and only the first one is good news.
/// </summary>
public sealed record GamePassProcessScan
{
    /// <summary>Everything found, one entry per distinct process name.</summary>
    public required IReadOnlyList<GamePassRunningProcess> Found { get; init; }

    /// <summary>
    /// True when the process list could not be read, so nothing here is evidence of anything. A
    /// scan that failed must never be reported as "nothing is running".
    /// </summary>
    public required bool Unknown { get; init; }

    /// <summary>A scan that found nothing and knew it - the shape used by tests and by callers that
    /// have no reason to look.</summary>
    public static GamePassProcessScan Nothing { get; } =
        new() { Found = Array.Empty<GamePassRunningProcess>(), Unknown = false };

    /// <summary>True when Abiotic Factor itself is running.</summary>
    public bool IsGameRunning => Found.Any(p => p.Role == GamePassProcessRole.Game);

    /// <summary>True when the Xbox app or a Gaming Services process is running.</summary>
    public bool IsCompanionRunning => Found.Any(p => p.Role != GamePassProcessRole.Game);

    /// <summary>The names of the running game processes (usually one).</summary>
    public IReadOnlyList<string> GameProcessNames
        => Found.Where(p => p.Role == GamePassProcessRole.Game).Select(p => p.Name).ToList();

    /// <summary>The names of the running Xbox app / Gaming Services processes.</summary>
    public IReadOnlyList<string> CompanionProcessNames
        => Found.Where(p => p.Role != GamePassProcessRole.Game).Select(p => p.Name).ToList();
}

/// <summary>Why writing into a Game Pass save right now could lose the edit.</summary>
public enum GamePassWriteRisk
{
    /// <summary>Xbox is holding a conflict for this save that it has not settled.</summary>
    UnresolvedConflict,

    /// <summary>Part of the save carries a state the format does not define, or is marked deleted -
    /// a container the service is entitled to take away.</summary>
    UnsafeContainerState,

    /// <summary>A container's state and its cloud version token disagree. The next write to that
    /// container corrects it, so this is worth saying but not worth refusing over.</summary>
    ContradictoryContainerState,

    /// <summary>Abiotic Factor is running.</summary>
    GameRunning,

    /// <summary>The Xbox app or Gaming Services is running.</summary>
    CompanionRunning,

    /// <summary>The process list could not be read, so nothing is known about what else is
    /// touching this save.</summary>
    ProcessScanUnavailable,
}

/// <summary>One thing that is wrong (or merely worth knowing) about writing to a save now.</summary>
/// <param name="Risk">Which hazard this is.</param>
/// <param name="Blocking">True when a write is refused unless the caller overrides it deliberately.</param>
/// <param name="Message">Player-facing wording: what is wrong and what to do about it.</param>
public sealed record GamePassWriteConcern(GamePassWriteRisk Risk, bool Blocking, string Message);

/// <summary>
/// Everything the editor knows about whether a Game Pass save can safely be written right now.
///
/// <para>Only two things are refused outright: a conflict Xbox has not settled, and a container in
/// a state that makes it disposable (undefined, or a deletion tombstone). Both mean the save is
/// already in an argument with the cloud before the player changes anything, and an edit written
/// into one is settled later, out of sight, and can be settled against them.</para>
///
/// <para>The Xbox app and Gaming Services running is a warning rather than a refusal on purpose:
/// on a Game Pass machine those processes are up essentially all the time (verified on a live
/// install with the game closed), so refusing on them would refuse every edit forever, which
/// teaches players to reach for the override on every save and makes it mean nothing.</para>
/// </summary>
public sealed class GamePassWriteCheck
{
    private GamePassWriteCheck(IReadOnlyList<GamePassWriteConcern> concerns)
    {
        Concerns = concerns;
        Blockers = concerns.Where(c => c.Blocking).ToList();
        Warnings = concerns.Where(c => !c.Blocking).ToList();
    }

    /// <summary>Everything found, blocking and not.</summary>
    public IReadOnlyList<GamePassWriteConcern> Concerns { get; }

    /// <summary>The concerns that refuse a write.</summary>
    public IReadOnlyList<GamePassWriteConcern> Blockers { get; }

    /// <summary>The concerns worth telling the player about that do not refuse a write.</summary>
    public IReadOnlyList<GamePassWriteConcern> Warnings { get; }

    /// <summary>True when nothing is blocking a write.</summary>
    public bool CanWrite => Blockers.Count == 0;

    /// <summary>A check with nothing wrong.</summary>
    public static GamePassWriteCheck Clear { get; } = new(Array.Empty<GamePassWriteConcern>());

    /// <summary>
    /// Builds a check from the facts about a save store and its machine. Kept separate from the
    /// store so the decision can be exercised directly against every combination, including ones
    /// that cannot be produced on the machine running the tests.
    /// </summary>
    /// <param name="hasUnresolvedConflicts">The store's index carries the unresolved-conflict flag.</param>
    /// <param name="unsafeStateContainers">Containers whose state is undefined or a deletion tombstone.</param>
    /// <param name="contradictoryStateContainers">Containers whose state disagrees with their cloud token.</param>
    /// <param name="scan">What is running on this machine (see <see cref="GamePassEnvironment.Scan"/>).</param>
    /// <param name="storeIsLive">
    /// True when this folder is the one the installed game actually saves into. A copy somewhere
    /// else is not the container a running title will overwrite on exit, so the game being open is
    /// only a hazard for the real store.
    /// </param>
    public static GamePassWriteCheck For(
        bool hasUnresolvedConflicts,
        IReadOnlyList<string> unsafeStateContainers,
        IReadOnlyList<string> contradictoryStateContainers,
        GamePassProcessScan scan,
        bool storeIsLive)
    {
        ArgumentNullException.ThrowIfNull(unsafeStateContainers);
        ArgumentNullException.ThrowIfNull(contradictoryStateContainers);
        ArgumentNullException.ThrowIfNull(scan);

        var concerns = new List<GamePassWriteConcern>();

        if (hasUnresolvedConflicts)
        {
            concerns.Add(new GamePassWriteConcern(
                GamePassWriteRisk.UnresolvedConflict,
                Blocking: true,
                "Xbox has an unsettled conflict for this save; launch the game and let it load and save "
                + "this world first, or run 'gamepass repair'."));
        }

        if (unsafeStateContainers.Count > 0)
        {
            concerns.Add(new GamePassWriteConcern(
                GamePassWriteRisk.UnsafeContainerState,
                Blocking: true,
                $"Part of this save is in a state Xbox may throw away ({string.Join(", ", unsafeStateContainers)}). "
                + "Launch the game and let it load and save this world first, or run 'gamepass repair'."));
        }

        if (contradictoryStateContainers.Count > 0)
        {
            concerns.Add(new GamePassWriteConcern(
                GamePassWriteRisk.ContradictoryContainerState,
                Blocking: false,
                "Part of this save disagrees with itself about whether Xbox has ever seen it "
                + $"({string.Join(", ", contradictoryStateContainers)}). Saving corrects that, and "
                + "'gamepass repair' corrects all of it at once."));
        }

        if (storeIsLive && scan.IsGameRunning)
        {
            concerns.Add(new GamePassWriteConcern(
                GamePassWriteRisk.GameRunning,
                Blocking: true,
                $"Abiotic Factor is running ({string.Join(", ", scan.GameProcessNames)}). It has this save "
                + "open and writes its own copy over yours when it closes. Quit the game, wait for the "
                + "Xbox app to finish syncing, and try again."));
        }

        if (scan.IsCompanionRunning)
        {
            concerns.Add(new GamePassWriteConcern(
                GamePassWriteRisk.CompanionRunning,
                Blocking: false,
                $"The Xbox app or its save-syncing service is running ({string.Join(", ", scan.CompanionProcessNames)}). "
                + "That is normal, but if you have just closed the game, give it a minute to finish syncing "
                + "before you save."));
        }

        if (scan.Unknown)
        {
            concerns.Add(new GamePassWriteConcern(
                GamePassWriteRisk.ProcessScanUnavailable,
                Blocking: false,
                "The editor could not check whether the game or the Xbox app is running, so make sure "
                + "both are closed before you save."));
        }

        return new GamePassWriteCheck(concerns);
    }

    /// <summary>The blocking concerns as one player-facing paragraph (empty when nothing blocks).</summary>
    public string BlockingMessage() => string.Join(" ", Blockers.Select(b => b.Message));

    /// <summary>Every concern, blocking first, as one line each. For logs and the command line.</summary>
    public IReadOnlyList<string> Lines()
        => Blockers.Concat(Warnings).Select(c => c.Message).ToList();
}

/// <summary>
/// A caller's explicit, recorded acceptance that a Game Pass save may be damaged or discarded by
/// writing to it now.
///
/// <para>Deliberately a token that has to be constructed by name rather than a <c>bool force =
/// false</c> parameter: an argument like that is trivially passed by a caller who has not read what
/// it means, and the failure it enables is silent and unrecoverable. Constructing this says, in the
/// caller's own source, both that the risk is understood and who accepted it.</para>
/// </summary>
public sealed class GamePassWriteOverride
{
    private GamePassWriteOverride(string reason) => Reason = reason;

    /// <summary>Who accepted the risk and why - carried into the log with the write.</summary>
    public string Reason { get; }

    /// <summary>
    /// Accepts that this save may be lost, and allows the write anyway.
    /// </summary>
    /// <param name="reason">
    /// Who accepted the risk and why (for instance "player confirmed the conflict warning"). Recorded
    /// in the log next to the write, because the damage this permits shows up days later.
    /// </param>
    public static GamePassWriteOverride AcceptRiskOfLosingThisSave(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new GamePassWriteOverride(reason);
    }
}

/// <summary>Thrown when a write into a Game Pass save is refused because the save is not in a
/// state where an edit would survive.</summary>
public sealed class GamePassUnsafeWriteException : InvalidOperationException
{
    /// <summary>Creates the exception with the check that refused the write.</summary>
    /// <param name="check">What was wrong.</param>
    public GamePassUnsafeWriteException(GamePassWriteCheck check)
        : base(MessageFor(check))
        => Check = check;

    private static string MessageFor(GamePassWriteCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.BlockingMessage();
    }

    /// <summary>Creates the exception with a message of its own.</summary>
    public GamePassUnsafeWriteException()
    {
    }

    /// <summary>Creates the exception with a message of its own.</summary>
    /// <param name="message">The refusal, in the player's words.</param>
    public GamePassUnsafeWriteException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and a cause.</summary>
    /// <param name="message">The refusal, in the player's words.</param>
    /// <param name="innerException">The underlying failure.</param>
    public GamePassUnsafeWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>What was wrong, when the exception was raised by a guard.</summary>
    public GamePassWriteCheck Check { get; } = GamePassWriteCheck.Clear;
}

/// <summary>
/// The machine around a Game Pass save: what else is running that could be writing it, and whether
/// a folder is the store the installed game actually uses.
///
/// <para>Everything here is best effort and never throws. A process list that cannot be read is
/// reported as unknown rather than as "nothing is running", because the whole point is to avoid
/// editing underneath something else.</para>
/// </summary>
public static class GamePassEnvironment
{
    // Matched as name prefixes, because the real names vary by build: the shipped game runs as
    // AbioticFactor-Win64-Shipping, and a live machine was observed running "XboxPcAppFT",
    // "gamingservices" and "gamingservicesnet" side by side.
    private static readonly (string Prefix, GamePassProcessRole Role)[] Watched =
    [
        ("AbioticFactor", GamePassProcessRole.Game),
        ("XboxPcApp", GamePassProcessRole.XboxApp),
        ("GamingServices", GamePassProcessRole.SyncService),
        ("XblGameSave", GamePassProcessRole.SyncService),
    ];

    /// <summary>
    /// Looks for the game, the Xbox app and the Connected Storage sync worker. Never throws: if the
    /// process list cannot be read the result is marked <see cref="GamePassProcessScan.Unknown"/>.
    /// </summary>
    public static GamePassProcessScan Scan()
    {
        System.Diagnostics.Process[] processes;
        try
        {
            processes = System.Diagnostics.Process.GetProcesses();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException
            or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            Diagnostics.EditorLog.Warn("GamePass",
                $"Could not read the process list to check whether the game is running: {ex.Message}");
            return new GamePassProcessScan { Found = Array.Empty<GamePassRunningProcess>(), Unknown = true };
        }

        var found = new List<GamePassRunningProcess>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var process in processes)
            {
                string name;
                try { name = process.ProcessName; }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    // A process that exited between listing and reading is not evidence of anything.
                    continue;
                }

                foreach (var (prefix, role) in Watched)
                {
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Add(name)) found.Add(new GamePassRunningProcess(name, role));
                    break;
                }
            }
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }

        return new GamePassProcessScan { Found = found, Unknown = false };
    }

    /// <summary>
    /// True when <paramref name="folder"/> sits inside one of this machine's Connected Storage
    /// roots, which is to say it is the folder the installed game saves into rather than a copy of
    /// one. Only the real store is the container a running game overwrites when it closes, so this
    /// is what decides whether "the game is running" is a hazard or just a fact.
    /// </summary>
    public static bool IsInsideConnectedStorage(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return false;
        try
        {
            var full = Path.GetFullPath(folder);
            foreach (var root in GamePassDiscovery.ContainerStoreRoots())
            {
                var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (full.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) return true;
                if (full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unresolvable path is not one of the known roots.
        }
        return false;
    }
}
