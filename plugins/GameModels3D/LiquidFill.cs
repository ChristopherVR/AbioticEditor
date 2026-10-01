using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Kismet;
using CUE4Parse.UE4.Objects.UObject;

namespace AbioticEditor.Plugins.GameModels3D;

/// <summary>
/// Where a liquid container's surface sits for a saved fill, the way the game places it:
/// <c>Deployed_LiquidContainer_ParentBP.RefreshLiquidLevelAppearance</c> sets the <c>WaterLevel</c>
/// component's relative location to <c>VLerp(Liquid_FillLocationMin, Liquid_FillLocationMax,
/// FillLevel / Liquid_MaxFill)</c> and hides it while the fill is 0. The three values are class
/// defaults, inherited from parents when a class does not set them.
/// </summary>
internal static class LiquidFill
{
    public const string SurfaceComponent = "WaterLevel";
    private const int MaxClassDepth = 16;

    /// <summary>
    /// The surface's relative location for <paramref name="level"/> (null to hide it), or no entry
    /// when the class is not a liquid container.
    /// </summary>
    public static IReadOnlyDictionary<string, FVector?>? SurfaceFor(IFileProvider provider, string classPath, int level)
    {
        if (!provider.TryLoadPackageObject(classPath, out var obj) || obj is not UStruct cls) return null;
        if (Default<int>(cls, "Liquid_MaxFill") is not { } max || max <= 0) return null;
        if (level <= 0) return new Dictionary<string, FVector?> { [SurfaceComponent] = null };
        var min = Default<FVector>(cls, "Liquid_FillLocationMin") ?? FVector.ZeroVector;
        var top = Default<FVector>(cls, "Liquid_FillLocationMax") ?? min;
        var t = Math.Clamp(level / (float)max, 0f, 1f);
        return new Dictionary<string, FVector?>
        {
            [SurfaceComponent] = new FVector(min.X + ((top.X - min.X) * t), min.Y + ((top.Y - min.Y) * t), min.Z + ((top.Z - min.Z) * t)),
        };
    }

    /// <summary>
    /// The surface material for a liquid, as the game picks it: the liquid's enum value (read from
    /// the user-defined enum asset named in the saved value, e.g. <c>E_LiquidType</c>) selects a
    /// case of the switch in the class's <c>RefreshLiquidTypeAppearance</c>, whose case values are
    /// local variables assigned material constants. Null when the case keeps the default or
    /// nothing matches.
    /// </summary>
    public static string? SurfaceMaterial(IFileProvider provider, string classPath, string liquidName)
    {
        if (!provider.TryLoadPackageObject(classPath, out var obj) || obj is not UStruct cls) return null;
        if (EnumValue(provider, liquidName) is not { } value) return null;
        var old = provider.ReadScriptData;
        provider.ReadScriptData = true;
        try
        {
            for (UStruct? current = cls; current is not null; current = current.SuperStruct?.Load<UStruct>())
            {
                if (current is not UClass) continue;
                var package = current.Owner;
                if (package is null) continue;
                // Reload so the script bytecode is read (packages loaded before kept none).
                var path = package.Name;
                if (!provider.TryLoadPackage(path, out var withScript)) continue;
                foreach (var function in withScript.GetExports().OfType<UFunction>().Where(f => f.Name == "RefreshLiquidTypeAppearance"))
                {
                    if (CaseMaterial(function, value) is { } material) return material;
                }
            }
        }
        finally
        {
            provider.ReadScriptData = old;
        }
        return null;
    }

    private static long? EnumValue(IFileProvider provider, string liquidName)
    {
        var enumName = liquidName.Contains("::", StringComparison.Ordinal) ? liquidName[..liquidName.IndexOf("::", StringComparison.Ordinal)] : "E_LiquidType";
        var valueName = liquidName[(liquidName.LastIndexOf(':') + 1)..];
        var path = provider.Files.Keys.FirstOrDefault(k => k.EndsWith("/" + enumName + ".uasset", StringComparison.OrdinalIgnoreCase));
        if (path is null || !provider.TryLoadPackage(path, out var package)) return null;
        var uenum = package.GetExports().OfType<UEnum>().FirstOrDefault();
        foreach (var (name, value) in uenum?.Names ?? [])
        {
            var text = name.Text;
            if (text[(text.LastIndexOf(':') + 1)..].Equals(valueName, StringComparison.Ordinal)) return value;
        }
        return null;
    }

    private static string? CaseMaterial(UFunction function, long value)
    {
        if (function.ScriptBytecode is not { Length: > 0 } script) return null;
        var expressions = script.SelectMany(Walk).ToList();
        var assigned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var let in expressions.OfType<EX_LetBase>())
        {
            if (let.Variable is EX_VariableBase variable && let.Assignment is EX_ObjectConst { Value: { IsNull: false } constant }
                && constant.ResolvedObject?.GetPathName() is { } materialPath)
            {
                assigned[variable.Variable.ToString()] = materialPath;
            }
        }
        foreach (var sw in expressions.OfType<EX_SwitchValue>())
        {
            foreach (var c in sw.Cases)
            {
                var index = c.CaseIndexValueTerm switch
                {
                    EX_ByteConst b => b.Value,
                    EX_IntConst i => i.Value,
                    EX_IntConstByte ib => ib.Value,
                    _ => -1L,
                };
                if (index != value) continue;
                return c.CaseTerm is EX_VariableBase v && assigned.TryGetValue(v.Variable.ToString(), out var material) ? material : null;
            }
        }
        return null;
    }

    /// <summary>Every expression in a statement, depth first (fields and arrays of expressions).</summary>
    private static IEnumerable<KismetExpression> Walk(KismetExpression root)
    {
        var stack = new Stack<KismetExpression>();
        stack.Push(root);
        var seen = new HashSet<KismetExpression>(ReferenceEqualityComparer.Instance);
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (!seen.Add(e)) continue;
            yield return e;
            foreach (var field in e.GetType().GetFields())
            {
                switch (field.GetValue(e))
                {
                    case KismetExpression child: stack.Push(child); break;
                    case KismetExpression[] children: foreach (var c in children) if (c is not null) stack.Push(c); break;
                    case FKismetSwitchCase[] cases:
                        foreach (var c in cases)
                        {
                            if (c.CaseIndexValueTerm is not null) stack.Push(c.CaseIndexValueTerm);
                            if (c.CaseTerm is not null) stack.Push(c.CaseTerm);
                        }
                        break;
                }
            }
        }
    }

    private static T? Default<T>(UStruct cls, string name) where T : struct
    {
        for (UStruct? current = cls; current is not null; current = current.SuperStruct?.Load<UStruct>())
        {
            if (current is UClass { ClassDefaultObject: { IsNull: false } cdo } && cdo.Load() is { } defaults && defaults.TryGetValue(out T value, name))
                return value;
        }
        return null;
    }
}
