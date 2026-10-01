using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>world object move|rotate|delete|duplicate</c> - staged base-building edits on the player-built
/// objects of a region save (Facility holds nearly all of them). Every command builds the same
/// <see cref="StagedBaseEdits"/> the app uses, prints the preview with <c>--dry-run</c> and otherwise
/// applies it all-or-nothing and writes through the normal backup path (previous file kept as <c>.bak</c>).
/// A blocking finding refuses the whole edit and writes nothing (exit code 1).
/// </summary>
internal static class PlacedObjectEditCommands
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Common(
        Argument<string> Save, Option<string[]> Keys, Option<bool> DryRun, Option<bool> Json);

    public static Command Build(Option<bool> quiet)
    {
        var cmd = new Command("object",
            "Move, rotate, delete or duplicate player-built placed objects (staged; --dry-run previews). "
            + "Coordinates are the save's own (centimetres); the game loads pieces exactly where they are written and does not check that they fit.");
        cmd.Subcommands.Add(BuildMove(quiet));
        cmd.Subcommands.Add(BuildRotate(quiet));
        cmd.Subcommands.Add(BuildDelete(quiet));
        cmd.Subcommands.Add(BuildDuplicate(quiet));
        return cmd;
    }

    private static Common AddCommon(Command cmd)
    {
        var save = new Argument<string>("save") { Description = "Path to a WorldSave_*.sav file." };
        var keys = new Option<string[]>("--key", "-k")
        {
            Description = "Object key (32 hex chars). Repeat, or comma-separate, to select several.",
            AllowMultipleArgumentsPerToken = true,
            Required = true,
        };
        var dry = new Option<bool>("--dry-run") { Description = "Print the preview and write nothing." };
        var json = new Option<bool>("--json") { Description = "Print the preview as JSON." };
        cmd.Arguments.Add(save);
        cmd.Options.Add(keys);
        cmd.Options.Add(dry);
        cmd.Options.Add(json);
        return new Common(save, keys, dry, json);
    }

    private static Option<double> Num(string name, string help)
        => new(name) { Description = help, DefaultValueFactory = _ => 0.0 };

    // ---------- move ----------

    private static Command BuildMove(Option<bool> quiet)
    {
        var cmd = new Command("move", "Move the selected objects by a delta (centimetres).");
        var c = AddCommon(cmd);
        var dx = Num("--dx", "Shift along X (cm).");
        var dy = Num("--dy", "Shift along Y (cm).");
        var dz = Num("--dz", "Shift along Z, up (cm).");
        cmd.Options.Add(dx);
        cmd.Options.Add(dy);
        cmd.Options.Add(dz);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = Open(parse.GetValue(c.Save));
            Report(edits.MoveBy(data, ParseKeys(parse.GetValue(c.Keys)), parse.GetValue(dx), parse.GetValue(dy), parse.GetValue(dz)));
            return Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "moved");
        }));
        return cmd;
    }

    // ---------- rotate ----------

    private static Command BuildRotate(Option<bool> quiet)
    {
        var cmd = new Command("rotate", "Turn the selected objects about the vertical axis around a pivot.");
        var c = AddCommon(cmd);
        var yaw = new Option<double>("--yaw") { Description = "Degrees to turn (positive = the direction the saved rotation's Z grows).", Required = true };
        var pivot = new Option<string>("--pivot")
        {
            Description = "centroid (default), an object key, or x,y,z.",
            DefaultValueFactory = _ => "centroid",
        };
        cmd.Options.Add(yaw);
        cmd.Options.Add(pivot);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = Open(parse.GetValue(c.Save));
            Report(edits.RotateYaw(data, ParseKeys(parse.GetValue(c.Keys)), parse.GetValue(yaw), ParsePivot(parse.GetValue(pivot))));
            return Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "rotated");
        }));
        return cmd;
    }

    // ---------- delete ----------

    private static Command BuildDelete(Option<bool> quiet)
    {
        var cmd = new Command("delete",
            "Delete player-built objects (level-placed objects are refused). Anything outside the deleted set "
            + "that still points at an object refuses the delete unless you choose a policy.");
        var c = AddCommon(cmd);
        var owned = PolicyOption("--owned-sockets", "Outlet records the objects own: drop (default), keep, refuse.", "drop");
        var inbound = PolicyOption("--inbound-plugs", "Other sockets that plug into a deleted object: refuse (default), drop (unplug), keep (leave dangling).", "refuse");
        var other = PolicyOption("--other-references", "Any other record naming a deleted object: refuse (default) or keep.", "refuse");
        var bed = PolicyOption("--bed-claims", "A claimed bed: refuse (default), drop, keep.", "refuse");
        var pads = PolicyOption("--teleporter-peers", "A pad whose tag other pads share: refuse (default), drop, keep.", "refuse");
        var noScan = new Option<bool>("--no-scan") { Description = "Do not scan the other WorldSave_*.sav files in the folder for references (faster, less safe)." };
        cmd.Options.Add(owned);
        cmd.Options.Add(inbound);
        cmd.Options.Add(other);
        cmd.Options.Add(bed);
        cmd.Options.Add(pads);
        cmd.Options.Add(noScan);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = Open(parse.GetValue(c.Save));
            if (!parse.GetValue(noScan)) edits.OtherSaves = ReadSiblings(path);
            var policy = new DeletePolicy
            {
                OwnedSocketRecords = ParsePolicy(parse.GetValue(owned)),
                InboundPlugs = ParsePolicy(parse.GetValue(inbound)),
                OtherReferences = ParsePolicy(parse.GetValue(other)),
                BedClaims = ParsePolicy(parse.GetValue(bed)),
                TeleporterPeers = ParsePolicy(parse.GetValue(pads)),
            };
            edits.StageDelete(ParseKeys(parse.GetValue(c.Keys)), policy);
            return Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "deleted");
        }));
        return cmd;
    }

    // ---------- duplicate ----------

    private static Command BuildDuplicate(Option<bool> quiet)
    {
        var cmd = new Command("duplicate",
            "Copy the selected objects (new keys, actor paths and outlet ids; links inside the selection are "
            + "remapped together). Containers start empty unless --copy-contents. Bed claims are never copied.");
        var c = AddCommon(cmd);
        var dx = Num("--dx", "Shift of the copies along X (cm).");
        var dy = Num("--dy", "Shift of the copies along Y (cm).");
        var dz = Num("--dz", "Shift of the copies along Z, up (cm).");
        var yaw = Num("--yaw", "Turn the copied group by this many degrees about the pivot before shifting.");
        var pivot = new Option<string>("--pivot") { Description = "centroid (default), an object key, or x,y,z.", DefaultValueFactory = _ => "centroid" };
        var contents = new Option<bool>("--copy-contents") { Description = "Copy stored items too (default: copies start empty)." };
        var power = PolicyOption("--external-power", "A copied outlet feeding a device outside the selection: drop (default), keep, refuse.", "drop");
        var tele = PolicyOption("--external-teleporters", "A copied pad whose tag is used outside the selection: drop (default, no tag), keep, refuse.", "drop");
        var keepTags = new Option<bool>("--keep-teleporter-tags") { Description = "Copied pad pairs keep the original tag instead of getting a fresh shared one." };
        foreach (var o in new Option[] { dx, dy, dz, yaw, pivot, contents, power, tele, keepTags }) cmd.Options.Add(o);
        cmd.SetAction(parse => Cli.Run(() =>
        {
            var (path, data, edits) = Open(parse.GetValue(c.Save));
            var policy = new DuplicatePolicy
            {
                Contents = parse.GetValue(contents) ? ContentsMode.Copy : ContentsMode.Empty,
                ExternalPowerLinks = ParsePolicy(parse.GetValue(power)),
                ExternalTeleporterPeers = ParsePolicy(parse.GetValue(tele)),
                RemapInternalTeleporterPairs = !parse.GetValue(keepTags),
            };
            edits.StageDuplicate(
                ParseKeys(parse.GetValue(c.Keys)),
                new PlacedVector(parse.GetValue(dx), parse.GetValue(dy), parse.GetValue(dz)),
                parse.GetValue(yaw), ParsePivot(parse.GetValue(pivot)), policy);
            return Finish(path, data, edits, parse.GetValue(c.DryRun), parse.GetValue(c.Json), parse.GetValue(quiet), "copied");
        }));
        return cmd;
    }

    // ---------- shared ----------

    private static Option<string> PolicyOption(string name, string help, string fallback)
        => new(name) { Description = help, DefaultValueFactory = _ => fallback };

    internal static (string Path, WorldSaveData Data, StagedBaseEdits Edits) Open(string? save)
    {
        var path = Cli.RequireFile(save, "save file");
        var data = WorldSaveReader.ReadFromFile(path);
        return (path, data, new StagedBaseEdits { PrimaryName = Path.GetFileName(path) });
    }

    private static List<string> ParseKeys(string[]? raw)
    {
        var keys = (raw ?? []).SelectMany(k => k.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0) throw new CliUserErrorException("select at least one object with --key.");
        return keys;
    }

    private static ReferencePolicy ParsePolicy(string? text)
        => text?.Trim().ToLowerInvariant() switch
        {
            "refuse" => ReferencePolicy.Refuse,
            "drop" => ReferencePolicy.Drop,
            "keep" => ReferencePolicy.Keep,
            _ => throw new CliUserErrorException($"'{text}' is not a policy (use refuse, drop or keep)."),
        };

    private static GroupPivot ParsePivot(string? text)
    {
        var t = (text ?? "centroid").Trim();
        if (t.Equals("centroid", StringComparison.OrdinalIgnoreCase)) return GroupPivot.Centroid;
        var parts = t.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 3
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
        {
            return GroupPivot.At(new PlacedVector(x, y, z));
        }
        if (t.Length == 32 && t.All(Uri.IsHexDigit)) return GroupPivot.OfObject(t);
        throw new CliUserErrorException($"'{t}' is not a pivot (use centroid, an object key, or x,y,z).");
    }

    internal static List<(string Name, WorldSaveData Data)> ReadSiblings(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var result = new List<(string, WorldSaveData)>();
        foreach (var f in Directory.GetFiles(dir, "WorldSave_*.sav").Order(StringComparer.Ordinal))
        {
            if (string.Equals(Path.GetFullPath(f), path, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                result.Add((Path.GetFileName(f), WorldSaveReader.ReadFromFile(f)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException)
            {
                Cli.Warn($"skipped {Path.GetFileName(f)} while scanning for references: {ex.Message}");
            }
        }
        return result;
    }

    private static void Report(GroupTransformResult result)
    {
        foreach (var (key, reason) in result.Skipped) Cli.Warn($"{key}: {reason}");
    }

    internal static int Finish(string path, WorldSaveData data, StagedBaseEdits edits, bool dryRun, bool json, bool quiet, string verb)
    {
        var preview = edits.Preview(data);
        if (dryRun)
        {
            if (json) Console.WriteLine(JsonSerializer.Serialize(preview, JsonOptions));
            else Console.Write(FormatPreview(preview, Path.GetFileName(path), dryRun: true));
            return preview.CanApply ? Cli.Ok : Cli.UserError;
        }

        var result = edits.ApplyTo(data);
        if (!result.Applied)
        {
            foreach (var issue in result.Issues.Where(i => i.IsBlocking))
            {
                Console.Error.WriteLine($"blocked [{issue.Code}]: {issue.Message}");
            }
            throw new CliUserErrorException(
                $"refused: the edit cannot be applied as staged; nothing was written to {Path.GetFileName(path)}. "
                + "Run with --dry-run to see everything, or choose a policy for the blocking items.");
        }

        foreach (var issue in result.Issues.Where(i => i.Severity == BaseEditSeverity.Warning))
        {
            Cli.Warn(issue.Message);
        }
        WorldSaveWriter.WriteToFile(data, path);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
        }
        else
        {
            var parts = new List<string>();
            if (result.Transformed.Count > 0) parts.Add($"{result.Transformed.Count} object(s) {(verb == "rotated" ? "rotated" : "moved")}");
            if (result.Deleted.Count > 0) parts.Add($"{result.Deleted.Count} deleted ({result.SocketRecordsRemoved} outlet record(s) removed)");
            if (result.Created.Count > 0) parts.Add($"{result.Created.Count} copied ({result.SocketRecordsCreated} outlet record(s) created)");
            if (result.PowerLinksChanged > 0 || (result.Created.Count == 0 && result.SocketRecordsCreated > 0))
                parts.Add($"{result.PowerLinksChanged} power link(s) changed");
            if (result.Deleted.Count == 0 && result.SocketRecordsRemoved > 0) parts.Add($"{result.SocketRecordsRemoved} leftover outlet record(s) removed");
            Cli.Info(quiet, $"{string.Join(", ", parts)}. Objects: {result.ObjectsBefore} -> {result.ObjectsAfter}. "
                + $"Wrote {Path.GetFileName(path)} (previous kept as {Path.GetFileName(path)}.bak).");
            foreach (var c in result.Created) Cli.Info(quiet, $"  {c.SourceKey} -> {c.NewKey}");
        }
        return Cli.Ok;
    }

    /// <summary>The text form of a preview (also used by <c>--dry-run</c>).</summary>
    internal static string FormatPreview(BaseEditPreview p, string name, bool dryRun)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{(dryRun ? "Dry run" : "Preview")} for {name}: objects {p.ObjectsBefore} -> {p.ObjectsAfter}; "
            + $"{p.Transforms.Count} moved/turned, {p.Deletions.Count} deleted, {p.Duplications.Count} copied; "
            + $"{p.ItemsDeleted} stored item(s) deleted, {p.ItemsCopied} copied, {p.ReferencesAffected} link(s) affected.");
        foreach (var t in p.Transforms)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  move   {t.Key} {t.ClassName}: {Fmt(t.Before)} -> {Fmt(t.After)}"
                + $"{(t.DistanceCm is { } d ? $"  ({d:F0} cm" : "  (")}{(t.YawDeltaDegrees is { } y ? $", yaw {y:+0.##;-0.##;0} deg)" : ")")}"
                + $"{(t.Blocked ? "  BLOCKED" : string.Empty)}");
        }
        foreach (var d in p.Deletions)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  delete {d.Key} {d.ClassName}: {d.Contents.Count} stored slot(s), "
                + $"{d.OwnedSocketIds.Count} outlet record(s), {d.InboundLinks.Count} inbound link(s){(d.Blocked ? "  BLOCKED" : string.Empty)}");
            foreach (var item in d.Contents.Take(8))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"           item {item.ItemId} x{item.Count} (inventory {item.InventoryIndex}, slot {item.SlotIndex})");
            }
            if (d.Contents.Count > 8) sb.AppendLine(CultureInfo.InvariantCulture, $"           ... {d.Contents.Count - 8} more");
            foreach (var a in d.Actions) sb.AppendLine(CultureInfo.InvariantCulture, $"           {a}");
        }
        foreach (var c in p.Duplications)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  copy   {c.SourceKey} -> {c.NewKey} {c.ClassName}: {Fmt(c.Before)} -> {Fmt(c.After)}"
                + $"{(c.Blocked ? "  BLOCKED" : string.Empty)}");
            if (c.NewActorPath is not null) sb.AppendLine(CultureInfo.InvariantCulture, $"           actor path {c.NewActorPath}");
            foreach (var s in c.Sockets)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"           outlet {s.OldId} -> {s.NewId}: plugged {s.PluggedBefore ?? "-"} -> {s.PluggedAfter ?? "-"} ({s.Note})");
            }
            if (c.TeleporterTagBefore is { } tb) sb.AppendLine(CultureInfo.InvariantCulture, $"           teleporter tag {tb} -> {c.TeleporterTagAfter}");
        }
        foreach (var l in p.PowerLinks)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  power  {l.SocketLabel}: {l.DeviceBefore ?? "-"} -> {l.DeviceAfter ?? "-"}"
                + $"{(l.CreatesRecord ? "  (new outlet record)" : string.Empty)}{(l.Blocked ? "  BLOCKED" : string.Empty)}");
            foreach (var f in l.FeedsCleared) sb.AppendLine(CultureInfo.InvariantCulture, $"           unplugged from {f}");
        }
        foreach (var c in p.SocketCleanups) sb.AppendLine(CultureInfo.InvariantCulture, $"  remove leftover outlet record {c}");
        if (p.Issues.Count > 0)
        {
            sb.AppendLine("Findings:");
            foreach (var i in p.Issues)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  [{i.Severity.ToString().ToUpperInvariant()}] {i.Code}: {i.Message}");
            }
        }
        if (p.ProximityHints.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Proximity hints ({ProximityHints.Disclaimer}):");
            foreach (var h in p.ProximityHints.Take(20))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {h.KeyA} {h.ClassA} / {h.KeyB} {h.ClassB}: {h.DistanceCm:F0} cm");
            }
        }
        sb.AppendLine(p.CanApply ? "Result: would apply." : "Result: would be REFUSED (nothing written).");
        return sb.ToString();
    }

    private static string Fmt(PlacedObjectTransform? t)
        => t?.Translation is { } v
            ? string.Create(CultureInfo.InvariantCulture, $"({v.X:F0}, {v.Y:F0}, {v.Z:F0}{(t.Rotation is { } r ? $"; yaw {r.YawDegrees:F1}" : string.Empty)})")
            : "(no location)";
}
