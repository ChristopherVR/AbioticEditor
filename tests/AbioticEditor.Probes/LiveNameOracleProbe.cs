using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using AbioticEditor.Core.Assets;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.MappingsProvider.Usmap;
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Objects.UObject;
using CUE4Parse.UE4.Versions;

namespace AbioticEditor.Tests;

/// <summary>
/// Research probe for live editing after a game update: writes every blueprint class and struct
/// member the installed game defines, the identifiers compiled into the game executable, and a
/// diff of the native type mappings against an older mappings file, so every name the live agent
/// uses can be checked against the game that is actually installed. Output-only; runs only when
/// LIVE_NAME_ORACLE_OUT names an output folder (LIVE_NAME_ORACLE_OLD_USMAP optionally names the
/// older mappings to diff against).
/// </summary>
public class LiveNameOracleProbe
{
    [Fact]
    public void Dump_GameNameOracle()
    {
        var outDir = Environment.GetEnvironmentVariable("LIVE_NAME_ORACLE_OUT");
        if (string.IsNullOrWhiteSpace(outDir)) return;
        Directory.CreateDirectory(outDir);

        var paks = AfInstallLocator.FindPaksDirectory();
        var mappingsPath = GameAssetProvider.FindConventionalMappings();
        Assert.NotNull(paks);
        Assert.NotNull(mappingsPath);

#pragma warning disable CS0618
        using var provider = new DefaultFileProvider(paks!, SearchOption.TopDirectoryOnly, isCaseInsensitive: true, new VersionContainer(EGame.GAME_UE5_4));
#pragma warning restore CS0618
        provider.MappingsContainer = new FileUsmapTypeMappingsProvider(mappingsPath!);
        provider.Initialize();
        provider.SubmitKey(new FGuid(), new FAesKey("0x0000000000000000000000000000000000000000000000000000000000000000"));

        // Blueprint classes and structs: one line per member, "Owner|Super|kind|Name".
        using (var writer = new StreamWriter(Path.Combine(outDir, "blueprint-members.txt"), false))
        {
            var keys = provider.Files.Keys
                .Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                            && k.Contains("/Content/", StringComparison.OrdinalIgnoreCase)
                            && !Regex.IsMatch(k, "/(Textures?|Meshes|Materials?|Audio|Sounds?|Animations?|Art|VFX|Particles|Maps|Levels|UI/Images|Fonts|Movies)/", RegexOptions.IgnoreCase))
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var failed = 0;
            foreach (var key in keys)
            {
                CUE4Parse.UE4.Assets.IPackage pkg;
                try { pkg = provider.LoadPackage(key[..^".uasset".Length]); }
                catch { failed++; continue; }
                IEnumerable<UObject> exports;
                try { exports = pkg.GetExports().ToList(); }
                catch { failed++; continue; }
                foreach (var export in exports)
                {
                    if (export is UFunction fn)
                    {
                        var owner = fn.Outer?.Name ?? "";
                        foreach (var p in fn.ChildProperties ?? [])
                            writer.WriteLine($"{owner}||param|{fn.Name}.{p.Name.Text}|{FieldType(p)}|{(p is CUE4Parse.UE4.Objects.UObject.FProperty fp ? fp.PropertyFlags.ToString() : "")}");
                        continue;
                    }
                    if (export is not UStruct st) continue;
                    var super = st.SuperStruct?.Name ?? "";
                    writer.WriteLine($"{export.Name}|{super}|type|{export.ExportType}");
                    foreach (var p in st.ChildProperties ?? []) writer.WriteLine($"{export.Name}|{super}|prop|{p.Name.Text}|{FieldType(p)}");
                    foreach (var f in st.Children ?? []) writer.WriteLine($"{export.Name}|{super}|func|{f.Name}");
                }
            }
            writer.WriteLine($"#packages={keys.Count} failed={failed}");
        }

        // Native property names from the installed mappings: "Owner|Super|prop|Name".
        var current = new FileUsmapTypeMappingsProvider(mappingsPath!).MappingsForGame!;
        using (var writer = new StreamWriter(Path.Combine(outDir, "native-members.txt"), false))
        {
            foreach (var (name, type) in current.Types)
            {
                writer.WriteLine($"{name}|{type.SuperType}|type|native");
                foreach (var p in type.Properties.Values) writer.WriteLine($"{name}|{type.SuperType}|prop|{p.Name}|{Describe(p.MappingType)}");
            }
            foreach (var (name, values) in current.Enums)
                foreach (var (k, v) in values) writer.WriteLine($"{name}||enum|{v}|{k}");
        }

        // Identifiers compiled into the executable (native classes, functions and properties).
        var exe = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(paks!)!, "..", "Binaries", "Win64"), "*-Shipping.exe").Single();
        var bytes = File.ReadAllBytes(exe);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ascii = new StringBuilder();
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'_') ascii.Append((char)b);
            else { if (ascii.Length >= 3) names.Add(ascii.ToString()); ascii.Clear(); }
        }
        var wide = new StringBuilder();
        for (var i = 0; i + 1 < bytes.Length; i += 2)
        {
            var c = (char)(bytes[i] | (bytes[i + 1] << 8));
            if (char.IsAsciiLetterOrDigit(c) || c == '_') wide.Append(c);
            else { if (wide.Length >= 3) names.Add(wide.ToString()); wide.Clear(); }
        }
        for (var i = 1; i + 1 < bytes.Length; i += 2)
        {
            var c = (char)(bytes[i] | (bytes[i + 1] << 8));
            if (char.IsAsciiLetterOrDigit(c) || c == '_') wide.Append(c);
            else { if (wide.Length >= 3) names.Add(wide.ToString()); wide.Clear(); }
        }
        File.WriteAllLines(Path.Combine(outDir, "exe-identifiers.txt"), names.Order(StringComparer.Ordinal));

        // Native layout changes against an older mappings file.
        var oldPath = Environment.GetEnvironmentVariable("LIVE_NAME_ORACLE_OLD_USMAP");
        if (oldPath is null || !File.Exists(oldPath)) return;
        var old = new FileUsmapTypeMappingsProvider(oldPath).MappingsForGame!;
        using (var writer = new StreamWriter(Path.Combine(outDir, "native-diff.txt"), false))
        {
            foreach (var (name, oldType) in old.Types.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                if (!current.Types.TryGetValue(name, out var newType)) { writer.WriteLine($"REMOVED TYPE {name}"); continue; }
                var newProps = newType.Properties.Values.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
                foreach (var p in oldType.Properties.Values)
                {
                    if (!newProps.TryGetValue(p.Name, out var np)) writer.WriteLine($"REMOVED PROP {name}.{p.Name}");
                    else if (Describe(np.MappingType) != Describe(p.MappingType)) writer.WriteLine($"CHANGED PROP {name}.{p.Name}: {Describe(p.MappingType)} -> {Describe(np.MappingType)}");
                }
                if (oldType.SuperType != newType.SuperType) writer.WriteLine($"CHANGED SUPER {name}: {oldType.SuperType} -> {newType.SuperType}");
            }
            foreach (var name in current.Types.Keys.Except(old.Types.Keys).Order(StringComparer.Ordinal)) writer.WriteLine($"ADDED TYPE {name}");
            foreach (var (name, values) in old.Enums)
            {
                if (!current.Enums.TryGetValue(name, out var nv)) { writer.WriteLine($"REMOVED ENUM {name}"); continue; }
                foreach (var (k, v) in values)
                    if (!nv.TryGetValue(k, out var now) || now != v) writer.WriteLine($"CHANGED ENUM {name}[{k}]: {v} -> {(nv.TryGetValue(k, out var x) ? x : "(gone)")}");
            }
        }
    }

    private static string FieldType(CUE4Parse.UE4.Objects.UObject.FField field) => field switch
    {
        CUE4Parse.UE4.Objects.UObject.FArrayProperty a => $"ArrayProperty<{(a.Inner is null ? "?" : FieldType(a.Inner))}>",
        CUE4Parse.UE4.Objects.UObject.FSetProperty s => $"SetProperty<{(s.ElementProp is null ? "?" : FieldType(s.ElementProp))}>",
        CUE4Parse.UE4.Objects.UObject.FMapProperty m => $"MapProperty<{(m.KeyProp is null ? "?" : FieldType(m.KeyProp))},{(m.ValueProp is null ? "?" : FieldType(m.ValueProp))}>",
        CUE4Parse.UE4.Objects.UObject.FStructProperty s => $"StructProperty<{s.Struct.ResolvedObject?.Name}>",
        CUE4Parse.UE4.Objects.UObject.FObjectProperty o => $"{o.GetType().Name[1..]}<{o.PropertyClass.ResolvedObject?.Name}>",
        CUE4Parse.UE4.Objects.UObject.FEnumProperty e => $"EnumProperty<{e.Enum.ResolvedObject?.Name}>",
        CUE4Parse.UE4.Objects.UObject.FByteProperty b => $"ByteProperty<{b.Enum.ResolvedObject?.Name}>",
        _ => field.GetType().Name[1..],
    };

    private static string Describe(PropertyType m) => m.Type + (m.StructType is null ? "" : $"<{m.StructType}>") + (m.EnumName is null ? "" : $"<{m.EnumName}>")
        + (m.InnerType is null ? "" : $"<{Describe(m.InnerType)}>") + (m.ValueType is null ? "" : $",<{Describe(m.ValueType)}>");
}
