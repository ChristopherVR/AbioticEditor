using AbioticEditor.Core.Items;
using AbioticEditor.Core.WorldSaves;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Objects;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.GameplayTags;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Core.Assets;

/// <summary>
/// What the game's own blueprint data says about one placed-object class, read from its class
/// defaults up the parent chain (the same way the game resolves an unset value).
/// </summary>
/// <param name="Chain">The class and its parents, the class itself first (e.g.
/// <c>Deployed_Freezer_C</c>, <c>Deployed_Container_ParentBP_C</c>, ...).</param>
/// <param name="RequiresPower">The class default <c>RequiresPower</c>: true for things that run
/// on power (benches, fridges, lights, turrets...). Null when no class in the chain sets it.</param>
/// <param name="SupportsUpgrades">The class default <c>SupportsUpgrades</c>: true for the benches
/// that take bench upgrades. Null when no class in the chain sets it (the game's default is no).</param>
/// <param name="ContainerRequirement">The <c>ContainerTagRequirement</c> of the class's
/// <c>ContainerInventory</c> component: which items its storage accepts. Null when the class has
/// no such component.</param>
public sealed record BlueprintClassFacts(
    IReadOnlyList<string> Chain,
    bool? RequiresPower,
    bool? SupportsUpgrades,
    ItemTagQuery? ContainerRequirement)
{
    /// <summary>True when <paramref name="className"/> (e.g. <c>Deployed_Bench_CookingStation_C</c>)
    /// is this class or one of its parents.</summary>
    public bool IsA(string className)
        => Chain.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase));
}

public sealed partial class GameAssetProvider
{
    private readonly Dictionary<string, BlueprintClassFacts?> _classFacts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string>? _blueprintPackagesByName;
    private BenchUpgradeScreenRules? _benchUpgradeScreen;
    private bool _benchUpgradeScreenRead;

    /// <summary>The name of the inventory component whose contents a placed object saves first.</summary>
    private const string ContainerComponentTemplate = "ContainerInventory_GEN_VARIABLE";

    /// <summary>
    /// The blueprint facts for a placed-object class, given its full class path
    /// (<c>/Game/Blueprints/.../Deployed_X.Deployed_X_C</c>) or just its class name
    /// (<c>Deployed_X_C</c>). Null when the class cannot be found in the game files. Answers are
    /// remembered, so asking again is cheap.
    /// </summary>
    public BlueprintClassFacts? GetClassFacts(string? classPathOrName)
    {
        if (string.IsNullOrWhiteSpace(classPathOrName)) return null;
        ThrowIfDisposed();
        lock (_providerLoadLock)
        {
            if (_classFacts.TryGetValue(classPathOrName, out var cached)) return cached;
            BlueprintClassFacts? facts = null;
            try
            {
                if (ResolveClassPath(classPathOrName) is { } path
                    && _provider.TryLoadPackageObject(path, out var obj) && obj is UStruct cls)
                {
                    facts = ReadClassFacts(cls);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                facts = null;
            }
            _classFacts[classPathOrName] = facts;
            return facts;
        }
    }

    /// <summary>
    /// How the game's bench upgrade screen (<c>W_BenchUpgradeScreen</c>) decides which upgrades a
    /// bench offers, or null when the game files do not have it. Read once.
    /// </summary>
    public BenchUpgradeScreenRules? GetBenchUpgradeScreenRules()
    {
        ThrowIfDisposed();
        lock (_providerLoadLock)
        {
            if (_benchUpgradeScreenRead) return _benchUpgradeScreen;
            _benchUpgradeScreenRead = true;
            try
            {
                _benchUpgradeScreen = ReadBenchUpgradeScreen();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _benchUpgradeScreen = null;
            }
            return _benchUpgradeScreen;
        }
    }

    /// <summary>A full class path as the provider loads it; a bare class name is looked up among the blueprints.</summary>
    private string? ResolveClassPath(string classPathOrName)
    {
        if (classPathOrName.StartsWith('/')) return classPathOrName;
        var name = classPathOrName.EndsWith("_C", StringComparison.Ordinal) ? classPathOrName[..^2] : classPathOrName;
        if (_blueprintPackagesByName is null)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in _provider.Files.Keys)
            {
                if (!key.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                    || !key.Contains("/Content/Blueprints/", StringComparison.OrdinalIgnoreCase)) continue;
                var file = key[(key.LastIndexOf('/') + 1)..^".uasset".Length];
                index.TryAdd(file, key[..^".uasset".Length]);
            }
            _blueprintPackagesByName = index;
        }
        return _blueprintPackagesByName.TryGetValue(name, out var package)
            ? $"{package}.{name}_C"
            : null;
    }

    private static BlueprintClassFacts ReadClassFacts(UStruct cls)
    {
        var chain = new List<string>();
        bool? requiresPower = null, supportsUpgrades = null;
        ItemTagQuery? requirement = null;
        UClass? componentClass = null;
        for (UStruct? current = cls; current is not null && chain.Count < 32; current = current.SuperStruct?.Load<UStruct>())
        {
            chain.Add(current.Name);
            if (current is UClass { ClassDefaultObject: { IsNull: false } cdoRef } && cdoRef.Load() is { } cdo)
            {
                if (requiresPower is null && cdo.TryGetValue(out bool power, "RequiresPower")) requiresPower = power;
                if (supportsUpgrades is null && cdo.TryGetValue(out bool upgrades, "SupportsUpgrades")) supportsUpgrades = upgrades;
            }
            // The storage component as this class sets it up (its own, or its override of a parent's).
            if (requirement is null && current.Owner is { } package
                && package.GetExportOrNull(ContainerComponentTemplate, StringComparison.OrdinalIgnoreCase) is { } component)
            {
                componentClass ??= component.Class?.Load() as UClass;
                requirement = ReadTagQuery(component, "ContainerTagRequirement");
            }
        }
        // A component that leaves the requirement alone keeps its own class default.
        if (requirement is null && componentClass is not null)
        {
            for (UStruct? current = componentClass; current is not null && requirement is null; current = current.SuperStruct?.Load<UStruct>())
            {
                if (current is UClass { ClassDefaultObject: { IsNull: false } cdoRef } && cdoRef.Load() is { } cdo)
                    requirement = ReadTagQuery(cdo, "ContainerTagRequirement");
            }
        }
        return new BlueprintClassFacts(chain, requiresPower, supportsUpgrades, requirement);
    }

    private static ItemTagQuery? ReadTagQuery(UObject owner, string property)
    {
        if (!owner.TryGetValue(out FStructFallback fallback, property)) return null;
        var query = new FGameplayTagQuery(fallback);
        return new ItemTagQuery(
            (query.TagDictionary ?? []).Select(t => t.TagName.Text).ToList(),
            query.QueryTokenStream ?? [],
            query.AutoDescription?.Trim());
    }

    /// <summary>
    /// Reads the upgrade screen: each upgrade entry widget names its <c>DT_BenchUpgrades</c> row,
    /// and the screen's event graph checks whether the bench is a particular class
    /// (<c>ClassIsChildOf</c>) and then shows or collapses entries for each answer.
    /// </summary>
    private BenchUpgradeScreenRules? ReadBenchUpgradeScreen()
    {
        var path = _provider.Files.Keys.FirstOrDefault(k => k.EndsWith("/W_BenchUpgradeScreen.uasset", StringComparison.OrdinalIgnoreCase));
        if (path is null) return null;
        var old = _provider.ReadScriptData;
        _provider.ReadScriptData = true;
        try
        {
            if (!_provider.TryLoadPackage(path, out var package) || package is null) return null;
            var exports = package.GetExports().ToList();
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var export in exports)
            {
                if (export.TryGetValue(out FStructFallback handle, "BenchUpgrade")
                    && handle.TryGetValue(out FName row, "RowName") && !row.IsNone)
                    entries[export.Name] = row.Text;
            }
            if (entries.Count == 0) return null;
            var rows = entries.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var graph = exports.OfType<UFunction>().FirstOrDefault(f => f.Name.StartsWith("ExecuteUbergraph_", StringComparison.Ordinal));
            if (graph?.ScriptBytecode is not { Length: > 0 } code) return new BenchUpgradeScreenRules(rows, null, [], []);

            for (var i = 0; i < code.Length; i++)
            {
                if (code[i] is not EX_LetBase { Assignment: EX_CallMath call }
                    || !call.StackNode.Name.Equals("ClassIsChildOf", StringComparison.Ordinal)
                    || call.Parameters.OfType<EX_ObjectConst>().FirstOrDefault() is not { } familyConst) continue;
                var jump = Array.FindIndex(code, i + 1, s => s is EX_JumpIfNot);
                if (jump < 0) break;
                var elseAt = (int)((EX_JumpIfNot)code[jump]).CodeOffset;
                var whenFamily = VisibilityRun(code, jump + 1, entries, stopAt: elseAt);
                var elseIndex = Array.FindIndex(code, s => s.StatementIndex == elseAt);
                var otherwise = elseIndex < 0 ? [] : VisibilityRun(code, elseIndex, entries, stopAt: int.MaxValue);
                if (whenFamily.Count == 0 || otherwise.Count == 0) continue;
                return new BenchUpgradeScreenRules(rows, familyConst.Value.Name,
                    whenFamily.Where(kv => kv.Value != 0).Select(kv => kv.Key).ToList(),
                    otherwise.Where(kv => kv.Value != 0).Select(kv => kv.Key).ToList());
            }
            return new BenchUpgradeScreenRules(rows, null, [], []);
        }
        finally
        {
            _provider.ReadScriptData = old;
        }
    }

    /// <summary>
    /// The upgrade rows a run of <c>SetVisibility</c> calls sets, with the visibility each gets
    /// (0 is visible). The run ends at the first statement that is not a visibility change.
    /// </summary>
    private static Dictionary<string, int> VisibilityRun(KismetExpression[] code, int start, Dictionary<string, string> entries, int stopAt)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = start; i < code.Length && code[i].StatementIndex < stopAt; i++)
        {
            if (code[i] is not EX_Context { ObjectExpression: EX_InstanceVariable target, ContextExpression: EX_VirtualFunction call }
                || !call.VirtualFunctionName.Text.Equals("SetVisibility", StringComparison.Ordinal)
                || call.Parameters.FirstOrDefault() is not EX_ByteConst value) break;
            var widget = target.Variable.New?.Path.LastOrDefault().Text ?? target.Variable.Old?.Name;
            if (widget is not null && entries.TryGetValue(widget, out var row)) result[row] = value.Value;
        }
        return result;
    }
}
