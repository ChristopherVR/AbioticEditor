using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Items;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// What the installed game's own blueprint data says about a placed object's class: whether it
/// runs on power, whether it takes bench upgrades (and which), and which items its storage
/// accepts. Every answer is optional: with no game install (the browser build, or a machine
/// without the game) each method returns "unknown" and callers fall back to what the save shows.
/// </summary>
public sealed class GameClassFactsService
{
    private GameAssetProvider? _provider;
    private bool _providerTried;
    private readonly object _sync = new();

    private GameAssetProvider? Provider
    {
        get
        {
            lock (_sync)
            {
                if (_providerTried) return _provider;
                _providerTried = true;
                try
                {
                    var provider = GameDataGate.CreateProvider();
                    _provider = provider is { HasMappings: true } ? provider : null;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    _provider = null;
                }
                return _provider;
            }
        }
    }

    /// <summary>The class facts for a class path or class name, or null when unknown.</summary>
    public BlueprintClassFacts? For(string? classPathOrName)
    {
        if (string.IsNullOrWhiteSpace(classPathOrName)) return null;
        try { return Provider?.GetClassFacts(classPathOrName); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    /// <summary>The game's bench upgrade screen rules, or null when unknown.</summary>
    public BenchUpgradeScreenRules? BenchScreen
    {
        get
        {
            try { return Provider?.GetBenchUpgradeScreenRules(); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
        }
    }

    /// <summary>
    /// True when this placed object takes bench upgrades. From the game data when it is there
    /// (the class default <c>SupportsUpgrades</c>); otherwise only a bench that already has an
    /// upgrade installed, or the crafting-bench family, counts.
    /// </summary>
    public bool TakesBenchUpgrades(WorldDeployable deployable, string? classPath = null)
    {
        ArgumentNullException.ThrowIfNull(deployable);
        if (For(classPath ?? deployable.ClassName) is { } facts) return facts.SupportsUpgrades == true;
        return deployable.IsCraftingBench
            || deployable.InstalledUpgrades.Any(row => BenchUpgradeCatalog.All.Any(u => BenchUpgradeCatalog.IsInstalled(deployable.InstalledUpgrades, u.Row)));
    }

    /// <summary>The upgrades to list for a bench: only those the game offers on it, plus any already installed.</summary>
    public IReadOnlyList<BenchUpgrade> BenchUpgradesFor(WorldDeployable deployable, string? classPath = null)
    {
        ArgumentNullException.ThrowIfNull(deployable);
        if (BenchScreen is { } screen && For(classPath ?? deployable.ClassName) is { } facts)
            return screen.UpgradesFor(facts.Chain, deployable.InstalledUpgrades);
        return BenchUpgradeCatalog.All;
    }

    /// <summary>
    /// True when the class runs on power according to the game data (<c>RequiresPower</c>),
    /// false when the game data says it does not, null when unknown.
    /// </summary>
    public bool? RequiresPower(string? classPathOrName) => For(classPathOrName)?.RequiresPower;

    /// <summary>Which items this class's storage accepts, or null when unknown (no limit is applied then).</summary>
    public ItemTagQuery? ContainerRequirement(string? classPathOrName) => For(classPathOrName)?.ContainerRequirement;
}
