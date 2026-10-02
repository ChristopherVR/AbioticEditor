using System.Collections.Concurrent;
using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.Plugins;
using AbioticEditor.Plugins.Scene;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Hands the 3D base view real game models when an optional plugin supplies them (an
/// <see cref="ISceneModelProvider"/>). Nothing here reads game files itself: without such a plugin
/// every call answers "not available" and the view keeps drawing boxes.
/// </summary>
/// <remarks>
/// Also installs <see cref="PluginHostEnvironment.GameAssets"/>, so a model plugin reads the game
/// through the host's one shared mount instead of racing it with a second one (see
/// <see cref="GameDataGate"/>).
/// </remarks>
public sealed class SceneModelHostService
{
    private readonly HostSettingsService _settings;
    private readonly ConcurrentDictionary<string, SceneClassModel?> _classes = new(StringComparer.Ordinal);

    public SceneModelHostService(HostSettingsService settings)
    {
        _settings = settings;
        PluginHostEnvironment.GameAssets = static () =>
        {
            try { return GameDataGate.CreateProvider(GameDataLanguageStore.Saved); }
            catch { return null; }
        };
    }

    /// <summary>The first loaded provider that reports itself available, or null.</summary>
    public PluginCapability<ISceneModelProvider>? Provider
    {
        get
        {
            _settings.EnsurePluginsLoaded();
            foreach (var capability in PluginManager.Shared.SceneModelProviders)
            {
                try
                {
                    if (capability.Value.IsAvailable) return capability;
                }
                catch (Exception ex)
                {
                    EditorLog.Warn("Scene", $"3D model provider '{capability.Value.Id}' failed its availability check: {ex.Message}");
                }
            }
            return null;
        }
    }

    /// <summary>What the view shows in its status line.</summary>
    public SceneModelStatus Status()
    {
        var provider = Provider;
        var installed = PluginManager.Shared.SceneModelProviders.Count > 0;
        return provider is null
            ? new SceneModelStatus(false, installed, null, null)
            : new SceneModelStatus(true, true, provider.Value.Title, provider.Plugin.Manifest.Name);
    }

    /// <summary>
    /// Describes each class (null for one the provider has no model for). Answers are cached for
    /// the process: a class's model only changes with a game update, which restarts the editor.
    /// </summary>
    public IReadOnlyDictionary<string, SceneClassModel?> DescribeClasses(IEnumerable<string> classPaths)
    {
        var paths = classPaths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Take(2000).ToList();
        using var foreground = Foreground();
        var result = new System.Collections.Concurrent.ConcurrentDictionary<string, SceneClassModel?>(StringComparer.Ordinal);
        // Each class is mostly a small cache file read (or, the first time, a game-file read), so a
        // batch is worked out in parallel. Half the cores at most: the first read of an area keeps
        // these busy for seconds, and taking every core starved the editor's own window, which then
        // could not even show its loading progress.
        Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = WorkerCount }, path => result[path] = DescribeOne(path));
        TidyWhenIdle();
        return new Dictionary<string, SceneClassModel?>(result, StringComparer.Ordinal);
    }

    private static int WorkerCount => Math.Max(2, Environment.ProcessorCount / 2);

    // ---- the view first ------------------------------------------------------------------------
    // The background warm-up and the view read the game through one shared lock. Left to compete,
    // the warm-up's workers kept taking it, and the view's first look at an area after starting the
    // editor waited up to two minutes for its level. Now every request from the view counts itself
    // in, and the warm-up waits between pieces while any is running.
    private int _foreground;

    [ThreadStatic] private static bool t_warmingUp;

    private ForegroundScope Foreground() => new(t_warmingUp ? null : this);

    private readonly struct ForegroundScope : IDisposable
    {
        private readonly SceneModelHostService? _owner;
        public ForegroundScope(SceneModelHostService? owner)
        {
            _owner = owner;
            if (owner is not null) Interlocked.Increment(ref owner._foreground);
        }
        public void Dispose() { if (_owner is not null) Interlocked.Decrement(ref _owner._foreground); }
    }

    /// <summary>Called by the warm-up before each piece: waits while the view is asking for something.</summary>
    private void YieldToView()
    {
        for (var waited = 0; Volatile.Read(ref _foreground) > 0 && waited < 120_000; waited += 25) Thread.Sleep(25);
    }

    /// <summary>Runs a warm-up step: it does not count as the view asking (the flag is reset after, as pool threads are shared).</summary>
    private static T AsWarmUp<T>(Func<T> step)
    {
        var was = t_warmingUp;
        t_warmingUp = true;
        try { return step(); }
        finally { t_warmingUp = was; }
    }

    /// <summary>True while a request from the view is being answered (for tests).</summary>
    internal bool ViewIsAsking => Volatile.Read(ref _foreground) > 0;

    private SceneClassModel? DescribeOne(string path)
    {
        if (Provider?.Value is not { } provider) return null;
        return _classes.GetOrAdd(path, p =>
        {
            try { return ParseModelKey(p) is (var cls, { } state) ? provider.DescribeClass(cls, state) : provider.DescribeClass(p); }
            catch (Exception ex)
            {
                EditorLog.Warn("Scene", $"No 3D model for {p}: {ex.Message}");
                return null;
            }
        });
    }

    // ---- getting the 3D view ready up front --------------------------------------------------
    /// <summary>
    /// How far preparing the 3D view has got (see <see cref="StartPreparing"/>): whether it is running
    /// or has finished, the provider's latest progress snapshot, and when it started.
    /// </summary>
    public sealed record PreparationState(bool Running, bool Finished, ScenePreparationProgress Progress, DateTime? StartedUtc)
    {
        public static PreparationState Idle { get; } = new(false, false, ScenePreparationProgress.None, null);

        public TimeSpan Elapsed => StartedUtc is { } started ? DateTime.UtcNow - started : TimeSpan.Zero;
    }

    private PreparationState _preparation = PreparationState.Idle;
    private CancellationTokenSource? _preparationStop;
    private int? _remaining;

    /// <summary>Raised (from a background thread, about twice a second while running) whenever <see cref="Preparation"/> changes.</summary>
    public event Action? PreparationChanged;

    public PreparationState Preparation => _preparation;

    /// <summary>
    /// How many level files still have to be read before the 3D view is quick everywhere; null when
    /// there is no game to read (or the provider cannot prepare). Worked out once, off the caller's thread.
    /// </summary>
    public async Task<int?> RemainingToPrepareAsync()
    {
        if (_preparation.Running) return Math.Max(0, _preparation.Progress.Total - _preparation.Progress.Done);
        if (_remaining is { } known) return known;
        if (OperatingSystem.IsBrowser()) return null;
        var count = await Task.Run(() =>
        {
            try { return Provider?.Value is ISceneModelPreparation prep ? prep.RemainingToPrepare() : (int?)null; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                EditorLog.Warn("Scene", $"Could not tell what the 3D view still needs to read: {ex.Message}");
                return null;
            }
        }).ConfigureAwait(false);
        _remaining = count;
        return count;
    }

    /// <summary>
    /// Reads every level file's index now, in the background at low priority, so the 3D view never
    /// waits on one later (the first time, and after each game update). Safe to call twice. The 3D
    /// view can be used meanwhile: a level it needs is read once for both, and its own reads go first.
    /// </summary>
    public void StartPreparing()
    {
        if (OperatingSystem.IsBrowser() || _preparation.Running || Provider?.Value is not ISceneModelPreparation prep) return;
        var stop = new CancellationTokenSource();
        _preparationStop = stop;
        _preparation = new PreparationState(true, false, ScenePreparationProgress.None, DateTime.UtcNow);
        PreparationChanged?.Invoke();

        // The provider keeps a snapshot; it is read here twice a second rather than reported on every
        // step, so a fast run never floods the page with redraws.
        var poll = new Timer(_ =>
        {
            ScenePreparationProgress now;
            try { now = prep.Progress; }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
            if (!_preparation.Running || now == _preparation.Progress) return;
            _preparation = _preparation with { Progress = now };
            PreparationChanged?.Invoke();
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));

        var thread = new Thread(() =>
        {
            try
            {
                prep.Prepare(stop.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                EditorLog.Warn("Scene", $"Preparing the 3D view stopped: {ex.Message}");
            }
            poll.Dispose();
            var last = prep.Progress;
            var finished = !stop.IsCancellationRequested;
            _remaining = finished ? 0 : null;
            _preparation = _preparation with { Running = false, Finished = finished, Progress = last };
            EditorLog.Info("Scene", finished
                ? $"Prepared the 3D view: {last.Done} level files read in {ElapsedText(_preparation.Elapsed)}."
                : $"Stopped preparing the 3D view after {last.Done} of {last.Total} level files.");
            PreparationChanged?.Invoke();
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "3D view preparation" };
        thread.Start();
    }

    /// <summary>A level file's name as a player reads it: "Facility_Dam_Hydroplant" becomes "Facility Dam Hydroplant".</summary>
    public static string AreaName(string? map) => string.IsNullOrWhiteSpace(map) ? string.Empty : map.Replace('_', ' ').Trim();

    /// <summary>A running time as minutes and seconds ("3:07"), or hours too when it runs that long.</summary>
    public static string ElapsedText(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? elapsed.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
        : elapsed.ToString(@"m\:ss", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Stops preparing after the level in hand; what was read stays read.</summary>
    public void StopPreparing() => _preparationStop?.Cancel();

    private readonly HashSet<string> _prewarmed = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts working out the given model keys in the background, at low priority, so the 3D view
    /// finds them ready. Called when a world save opens: the first read of an area's models from
    /// the game files takes tens of seconds, and most of it can happen before the 3D view is opened.
    /// Keys already asked for are skipped; nothing is returned or awaited.
    /// </summary>
    public void Prewarm(Func<IEnumerable<string>> keys, string? region = null, Func<IEnumerable<float[]>>? levelCentres = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (Provider is null || OperatingSystem.IsBrowser()) return;
        var thread = new Thread(() =>
        {
            try
            {
                List<string> todo;
                lock (_prewarmed) todo = keys().Where(k => !string.IsNullOrWhiteSpace(k) && _prewarmed.Add(k)).ToList();
                Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, WorkerCount / 2) }, key => { YieldToView(); DescribeOne(key); });
                if (region is not null && levelCentres is not null) PrewarmLevel(region, levelCentres());
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                EditorLog.Warn("Scene", $"Preparing 3D models in the background stopped: {ex.Message}");
            }
        })
        { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "3D model warm-up" };
        thread.Start();
    }

    /// <summary>Metres around each centre the level is prepared for (the view's default radius).</summary>
    public const float PrewarmLevelRadiusM = 40f;

    /// <summary>
    /// Prepares the level around the given viewer-space points (the save's bases): reads the level's
    /// own data for the area once, then bakes each level piece's mesh and textures into the disk cache.
    /// Turning the level on in the 3D view, or "Show in 3D" on something nearby, then finds it ready
    /// instead of spending ten seconds or more reading it.
    /// </summary>
    private void PrewarmLevel(string region, IEnumerable<float[]> centres)
    {
        // First every sub-level of the region is indexed (a query over the whole region starts that,
        // then later queries report how many are still being read): a first look anywhere in the area,
        // such as "Show in 3D" on a door far from any base, otherwise waited seconds for its part.
        const float Everywhere = 1_000_000f;
        var whole = new SceneLevelQuery(region, [-Everywhere, -Everywhere, -Everywhere], [Everywhere, Everywhere, Everywhere], 1);
        var waitedUntil = DateTime.UtcNow.AddMinutes(5);
        while (AsWarmUp(() => DescribeLevel(whole)) is { PendingMaps: > 0 } && DateTime.UtcNow < waitedUntil) { Thread.Sleep(2000); YieldToView(); }

        var assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in centres.Where(c => c is { Length: 3 }).Take(12))
        {
            var r = PrewarmLevelRadiusM;
            YieldToView();
            var slice = AsWarmUp(() => DescribeLevel(new SceneLevelQuery(region, [c[0] - r, c[1] - r / 2, c[2] - r], [c[0] + r, c[1] + r / 2, c[2] + r], 25000)));
            if (slice is null) continue;
            foreach (var batch in slice.Batches)
            {
                assets.Add(batch.Mesh);
                foreach (var m in batch.Materials)
                {
                    if (m.Texture is { } t) assets.Add(t);
                    foreach (var layer in m.Layers ?? []) if (layer.Texture is { } lt) assets.Add(lt);
                }
            }
        }
        Parallel.ForEach(assets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, WorkerCount / 2) }, id => { YieldToView(); AsWarmUp(() => OpenAsset(id)); });
        EditorLog.Info("Scene", $"Prepared {assets.Count} level pieces and textures for {region} in the background.");
    }

    /// <summary>
    /// The viewer asks for an object's model as its class path followed by the parts of its state
    /// that change how it looks: <c>#paint=&lt;EPaintColor value&gt;</c> and
    /// <c>#crops=&lt;spot&gt;.&lt;crop row&gt;.&lt;stage&gt;,...</c> and <c>#liquid=&lt;level&gt;</c>. A plain class path has no state, and a
    /// key with anything else after the class path is passed on unchanged.
    /// </summary>
    public static (string ClassPath, SceneObjectState? State) ParseModelKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var at = key.IndexOf('#', StringComparison.Ordinal);
        if (at <= 0) return (key, null);
        int? paint = null;
        int? liquid = null;
        string? fluid = null;
        List<SceneCrop>? crops = null;
        foreach (var part in key[(at + 1)..].Split('#', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith(PaintPart, StringComparison.Ordinal) && TryNumber(part[PaintPart.Length..], out var p))
            {
                paint = p;
            }
            else if (part.StartsWith(LiquidPart, StringComparison.Ordinal) && TryNumber(part[LiquidPart.Length..], out var l))
            {
                liquid = l;
            }
            else if (part.StartsWith(FluidPart, StringComparison.Ordinal) && part[FluidPart.Length..] is { Length: > 0 and <= 80 } name
                     && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                fluid = name;
            }
            else if (part.StartsWith(CropsPart, StringComparison.Ordinal))
            {
                foreach (var spot in part[CropsPart.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = spot.Split('.');
                    if (bits.Length != 3 || !TryNumber(bits[0], out var index) || !TryNumber(bits[2], out var stage)
                        || bits[1].Length is 0 or > 80 || !bits[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    {
                        return (key, null);
                    }
                    (crops ??= []).Add(new SceneCrop(index, bits[1], stage));
                }
            }
            else
            {
                return (key, null);
            }
        }
        return (key[..at], new SceneObjectState(paint, crops, liquid, fluid));
    }

    private static bool TryNumber(string text, out int value)
        => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value);

    /// <summary>Paint part of a model key (see <see cref="ParseModelKey"/>).</summary>
    public const string PaintPart = "paint=";

    /// <summary>Crops part of a model key (see <see cref="ParseModelKey"/>).</summary>
    public const string CropsPart = "crops=";

    /// <summary>Liquid part of a model key: how much a liquid container holds (see <see cref="ParseModelKey"/>).</summary>
    public const string LiquidPart = "liquid=";

    /// <summary>Fluid part of a model key: which liquid, as its enum value name (see <see cref="ParseModelKey"/>).</summary>
    public const string FluidPart = "fluid=";

    /// <summary>The level geometry around a base, or null when the provider does not draw levels.</summary>
    public SceneLevelSlice? DescribeLevel(SceneLevelQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var provider = Provider?.Value;
        if (provider is null || string.IsNullOrWhiteSpace(query.Region)) return null;
        if (query.Min is not { Length: 3 } || query.Max is not { Length: 3 }) return null;
        var capped = query with { MaxInstances = Math.Clamp(query.MaxInstances, 1, 200_000) };
        using var foreground = Foreground();
        TidyWhenIdle();
        try { return provider.DescribeLevel(capped); }
        catch (Exception ex)
        {
            EditorLog.Warn("Scene", $"Level geometry for {query.Region} failed: {ex.Message}");
            return null;
        }
    }

    private Timer? _tidyTimer;
    private const int TidyAfterMs = 4000;

    /// <summary>
    /// Loading a view reads and serves hundreds of large buffers (meshes, textures, level pieces).
    /// The runtime only gives that space back in a full collection, which an idle desktop app rarely
    /// runs, so it stayed committed (127 MB on the Facility). A few seconds after the last request,
    /// one compacting full collection hands it back. Each request pushes the moment back.
    /// </summary>
    private void TidyWhenIdle()
    {
        if (OperatingSystem.IsBrowser()) return;
        lock (this)
        {
            _tidyTimer ??= new Timer(_ =>
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            });
            _tidyTimer.Change(TidyAfterMs, Timeout.Infinite);
        }
    }

    /// <summary>A mesh or texture by the id a model referenced, or null.</summary>
    public SceneAsset? OpenAsset(string assetId)
    {
        var provider = Provider?.Value;
        if (provider is null || string.IsNullOrWhiteSpace(assetId) || assetId.Length > 512) return null;
        using var foreground = Foreground();
        TidyWhenIdle();
        try { return provider.OpenAsset(assetId); }
        catch (Exception ex)
        {
            EditorLog.Warn("Scene", $"3D asset {assetId} failed: {ex.Message}");
            return null;
        }
    }
}

/// <summary>Whether game models are on offer, and from which plugin.</summary>
/// <param name="Available">A provider is loaded and can read the game.</param>
/// <param name="Installed">A provider plugin is loaded at all (it may still lack a game install).</param>
/// <param name="Title">The provider's own name.</param>
/// <param name="Plugin">The plugin that supplies it.</param>
public sealed record SceneModelStatus(bool Available, bool Installed, string? Title, string? Plugin);
