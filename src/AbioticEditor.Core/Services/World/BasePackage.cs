using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Core.WorldSaves;

/// <summary>One piece carried in a <see cref="BasePackage"/>, as the package's readable summary lists it.</summary>
/// <param name="Key">The piece's map key in the world it came from.</param>
/// <param name="ClassName">Blueprint class name (<c>Deployed_..._C</c>), when known.</param>
/// <param name="ClassPath">Full class path, when known (lets a viewer draw the game's own model).</param>
/// <param name="Position">Where it stood (save space, centimetres).</param>
/// <param name="YawDegrees">Which way it faced, display only.</param>
/// <param name="StoredItems">Items stored inside it (stack counts added up).</param>
/// <param name="CustomName">The name the player gave it, when any.</param>
public sealed record BasePackagePiece(
    string Key,
    string? ClassName,
    string? ClassPath,
    PlacedVector? Position,
    double? YawDegrees,
    int StoredItems,
    string? CustomName);

/// <summary>A piece that could not go into a package, and why.</summary>
public sealed record BasePackageSkip(string Key, string Reason);

/// <summary>
/// A group of player-built pieces lifted out of one world so it can be put down in another: what
/// "move a base" carries. It is a small detached save holding only those pieces (with everything
/// inside them and their own power outlet records), plus a readable summary. Putting it down is
/// the copy-from-another-world duplication (<see cref="StagedBaseEdits.StageTransplant"/>), so the
/// pieces get fresh ids in the target world, links between them are kept, and links to anything
/// left behind are dropped.
/// </summary>
/// <remarks>
/// <para>The file form (<see cref="ToJson"/> / <see cref="Parse(byte[])"/>) is JSON: the summary in
/// plain fields, and the pieces' exact save records as a base64 GVAS blob, so nothing the editor
/// does not model is lost on the way.</para>
/// <para>Only pieces a player built are carried. Pieces that came with the level belong to the
/// level, and the game would put them back from its own files.</para>
/// </remarks>
public sealed class BasePackage
{
    /// <summary>The value of the file's <c>format</c> field.</summary>
    public const string FormatName = "abiotic-editor-base";

    /// <summary>The file version this build writes and reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The suggested file extension.</summary>
    public const string FileExtension = ".abase.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly byte[] _saveBytes;
    private WorldSaveData? _donor;

    private BasePackage(
        byte[] saveBytes, IReadOnlyList<BasePackagePiece> pieces, string? sourceWorld, string? areaFile,
        IReadOnlyList<string> levelPackages, DateTimeOffset createdUtc)
    {
        _saveBytes = saveBytes;
        Pieces = pieces;
        SourceWorld = sourceWorld;
        AreaFile = areaFile;
        LevelPackages = levelPackages;
        CreatedUtc = createdUtc;
    }

    /// <summary>The pieces carried.</summary>
    public IReadOnlyList<BasePackagePiece> Pieces { get; }

    /// <summary>The name of the world the pieces came from (its folder name), when known.</summary>
    public string? SourceWorld { get; }

    /// <summary>The save file of the area they came from (<c>WorldSave_Facility.sav</c>), when known.</summary>
    public string? AreaFile { get; }

    /// <summary>The level packages the pieces' actor paths name. A target world must be on the same level.</summary>
    public IReadOnlyList<string> LevelPackages { get; }

    /// <summary>When the package was made.</summary>
    public DateTimeOffset CreatedUtc { get; }

    /// <summary>Items stored in all the pieces together.</summary>
    public int StoredItems => Pieces.Sum(p => p.StoredItems);

    /// <summary>The middle of the pieces (average position), or null when none has one.</summary>
    public PlacedVector? Centre
    {
        get
        {
            var at = Pieces.Where(p => p.Position is not null).Select(p => p.Position!.Value).ToList();
            return at.Count == 0 ? null : new PlacedVector(at.Average(p => p.X), at.Average(p => p.Y), at.Average(p => p.Z));
        }
    }

    /// <summary>The lowest point any piece stands on (its Z), or null.</summary>
    public double? FloorZ => Pieces.Where(p => p.Position is not null).Select(p => (double?)p.Position!.Value.Z).Min();

    /// <summary>The carried pieces' records as a save the duplication planner reads its sources from.</summary>
    internal WorldSaveData Donor
    {
        get
        {
            if (_donor is null)
            {
                using var ms = new MemoryStream(_saveBytes, writable: false);
                _donor = WorldSaveReader.ReadFromStream(ms);
            }
            return _donor;
        }
    }

    /// <summary>
    /// Lifts <paramref name="keys"/> out of <paramref name="source"/> into a package. Pieces a player did
    /// not build, unknown keys and pieces with no saved position are left out and listed in
    /// <paramref name="skipped"/>. <paramref name="currentTransform"/> supplies a piece's staged (not yet
    /// saved) place, so a piece moved in the editor travels from where it was put. Reads only.
    /// </summary>
    /// <exception cref="InvalidOperationException">No piece could be carried.</exception>
    public static BasePackage Create(
        WorldSaveData source,
        IEnumerable<string> keys,
        string? sourceWorld,
        string? areaFile,
        out IReadOnlyList<BasePackageSkip> skipped,
        Func<string, PlacedObjectTransform?>? currentTransform = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keys);
        var skips = new List<BasePackageSkip>();
        var carried = new List<string>();
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            var props = WorldMapAccessor.FindEntry(source.Raw, "DeployedObjectMap", key);
            if (props is null) skips.Add(new(key, "not-in-save"));
            else if (PlacedObjectCensus.KeyShape(key) != "guid32" || props.TryGetBool("DeployedByPlayer_") != true) skips.Add(new(key, "level-placed"));
            else if (PlacedObjectCensus.ReadTransform(props)?.Translation is null) skips.Add(new(key, "no-position"));
            else carried.Add(key);
        }
        skipped = skips;
        if (carried.Count == 0) throw new InvalidOperationException("None of the chosen pieces can be carried.");

        var set = carried.ToHashSet(StringComparer.Ordinal);
        var sockets = PlacedPowerRecords.Read(source)
            .Where(s => s.OwnerKey is { } owner && set.Contains(owner))
            .Select(s => s.Id)
            .ToHashSet(StringComparer.Ordinal);
        var detached = PlacedObjectCloner.CreateDonor(source.Raw, new Dictionary<string, IReadOnlySet<string>?>(StringComparer.Ordinal)
        {
            ["DeployedObjectMap"] = set,
            ["PowerSocketMap"] = sockets,
        });
        // Only the two maps the pieces live in travel; the rest of the area (its story state, the
        // level's own records) stays behind, which keeps the file small and private to the base.
        if (detached.Properties is { } top)
        {
            for (var i = top.Count - 1; i >= 0; i--)
            {
                var n = top[i].Name?.Value ?? string.Empty;
                if (!n.StartsWith("DeployedObjectMap", StringComparison.Ordinal) && !n.StartsWith("PowerSocketMap", StringComparison.Ordinal)) top.RemoveAt(i);
            }
        }

        // A piece the player moved in the editor (not saved yet) travels from where it now stands.
        if (currentTransform is not null)
        {
            foreach (var key in carried)
            {
                if (currentTransform(key) is not { } now
                    || WorldMapAccessor.FindEntry(detached, "DeployedObjectMap", key) is not { } props
                    || PlacedObjectCensus.ReadTransform(props) is not { } saved) continue;
                var t = now.Translation is { } nt && saved.Translation is { } st && nt != st ? now.Translation : null;
                var r = now.Rotation is { } nr && saved.Rotation is { } sr && nr != sr ? now.Rotation : null;
                if (t is not null || r is not null) WorldSaveWriter.ApplyTransformToProps(props, t, r);
            }
        }

        using var buffer = new MemoryStream();
        detached.WriteTo(buffer);
        var bytes = buffer.ToArray();
        var package = FromBytes(bytes, sourceWorld, areaFile, DateTimeOffset.UtcNow);
        return package;
    }

    /// <summary>Builds the summary from the carried records themselves, so a file and a fresh package agree.</summary>
    private static BasePackage FromBytes(byte[] bytes, string? sourceWorld, string? areaFile, DateTimeOffset created)
    {
        WorldSaveData donor;
        using (var ms = new MemoryStream(bytes, writable: false)) donor = WorldSaveReader.ReadFromStream(ms);
        var pieces = new List<BasePackagePiece>();
        var packages = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in WorldMapAccessor.Entries(donor.Raw, "DeployedObjectMap"))
        {
            var transform = PlacedObjectCensus.ReadTransform(e.Props);
            var classPath = PlacedObjectCensus.ClassPathOf(e.Props);
            var items = PlacedObjectContents.ReadItems(e.Props).Sum(i => Math.Max(i.Count, 1));
            var name = e.Props.GetString("CustomTextDisplay_");
            if (name is not null && (name.Length == 0 || name.Contains(WorldDeployable.ClaimSeparator, StringComparison.Ordinal))) name = null;
            pieces.Add(new BasePackagePiece(e.Key, PlacedObjectCensus.ClassNameOf(classPath), classPath,
                transform?.Translation, transform?.Rotation?.YawDegrees, items, name));
            if (WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_") is { Package: { Length: > 0 } pkg }) packages.Add(pkg);
        }
        if (pieces.Count == 0) throw new InvalidDataException("The base file holds no pieces.");
        return new BasePackage(bytes, pieces, sourceWorld, areaFile, packages.ToList(), created) { _donor = donor };
    }

    // ---------- file form ----------

    private sealed record FileForm(
        string Format,
        int Version,
        DateTimeOffset CreatedUtc,
        string? SourceWorld,
        string? AreaFile,
        IReadOnlyList<string>? LevelPackages,
        int PieceCount,
        int StoredItems,
        IReadOnlyList<BasePackagePiece>? Pieces,
        string? SaveEncoding,
        string Save);

    /// <summary>The package as a JSON file (UTF-8).</summary>
    public byte[] ToJson()
        => JsonSerializer.SerializeToUtf8Bytes(new FileForm(
            FormatName, CurrentVersion, CreatedUtc, SourceWorld, AreaFile, LevelPackages, Pieces.Count, StoredItems, Pieces,
            "gzip", Convert.ToBase64String(Gzip(_saveBytes))), JsonOptions);

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) zip.Write(bytes);
        return output.ToArray();
    }

    private static byte[] Gunzip(byte[] bytes)
    {
        using var input = new MemoryStream(bytes, writable: false);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zip.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>A file name for the package: the world, the area and the piece count.</summary>
    public string SuggestedFileName()
    {
        var area = AreaFile is { } a && a.StartsWith("WorldSave_", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(a)["WorldSave_".Length..]
            : "Base";
        var world = string.IsNullOrWhiteSpace(SourceWorld) ? "World" : SourceWorld;
        var stem = string.Create(CultureInfo.InvariantCulture, $"{world} {area} base ({Pieces.Count} pieces)");
        foreach (var c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        return stem + FileExtension;
    }

    /// <summary>Reads a package file. Throws <see cref="InvalidDataException"/> when it is not one.</summary>
    public static BasePackage Parse(byte[] json)
    {
        ArgumentNullException.ThrowIfNull(json);
        FileForm? form;
        try { form = JsonSerializer.Deserialize<FileForm>(json, JsonOptions); }
        catch (JsonException ex) { throw new InvalidDataException("This is not a base file from the editor.", ex); }
        if (form is null || !string.Equals(form.Format, FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("This is not a base file from the editor.");
        }
        var gzip = string.Equals(form.SaveEncoding, "gzip", StringComparison.OrdinalIgnoreCase);
        if (form.Version > CurrentVersion || (!gzip && form.SaveEncoding is { Length: > 0 }))
        {
            throw new InvalidDataException("This base file was made by a newer version of the editor. Update the editor to open it.");
        }
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(form.Save ?? string.Empty);
            if (gzip) bytes = Gunzip(bytes);
        }
        catch (Exception ex) when (ex is FormatException or IOException or InvalidDataException)
        {
            throw new InvalidDataException("The base file is damaged (its pieces cannot be read).", ex);
        }
        try
        {
            return FromBytes(bytes, form.SourceWorld, form.AreaFile, form.CreatedUtc);
        }
        catch (Exception ex) when (ex is not InvalidDataException and not OutOfMemoryException)
        {
            throw new InvalidDataException("The base file is damaged (its pieces cannot be read).", ex);
        }
    }

    /// <summary>Reads a package file from a stream.</summary>
    public static BasePackage Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Parse(ms.ToArray());
    }

    // ---------- fitting it into a target world ----------

    /// <summary>
    /// True when the pieces come from a different map than <paramref name="target"/> (another area of the
    /// game with its own level, not just another sector of the Facility). Then their old spot means
    /// nothing in the target, so a place must be chosen there; the pieces are put into the level the
    /// target keeps its player-built pieces in.
    /// </summary>
    public bool IsOtherMap(WorldSaveData target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var here = LevelsOf(target, playerBuiltOnly: false);
        return here.Count > 0 && LevelPackages.Any(p => !here.Contains(p));
    }

    /// <summary>True when nothing the player built is in <paramref name="target"/> yet (the level is then taken from the area's own pieces).</summary>
    public static bool HasNoPlayerPieces(WorldSaveData target)
        => LevelsOf(target ?? throw new ArgumentNullException(nameof(target)), playerBuiltOnly: true).Count == 0;

    private static HashSet<string> LevelsOf(WorldSaveData save, bool playerBuiltOnly)
    {
        var levels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in WorldMapAccessor.Entries(save.Raw, "DeployedObjectMap"))
        {
            if (playerBuiltOnly && (PlacedObjectCensus.KeyShape(e.Key) != "guid32" || e.Props.TryGetBool("DeployedByPlayer_") != true)) continue;
            if (WorldMapAccessor.GetSoftObjectPath(e.Props, "ActorPath_") is { Package: { Length: > 0 } pkg }) levels.Add(pkg);
        }
        return levels;
    }
}
