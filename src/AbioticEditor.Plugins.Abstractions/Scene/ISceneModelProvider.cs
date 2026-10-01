namespace AbioticEditor.Plugins.Scene;

/// <summary>
/// Supplies real 3D models to the editor's 3D base view. Without a provider the view draws a
/// coloured box per object; with one, each placed object is drawn with the parts the provider
/// describes for its class, and the level around a base can be drawn for context.
///
/// <para>
/// <b>Coordinate space.</b> Everything a provider returns is already in the <i>viewer</i> space
/// the 3D view uses: right-handed, Y up, metres. A save's Unreal space (left-handed, Z up,
/// centimetres) maps to it as <c>viewer = (X, Z, Y) / 100</c>; see <c>PlacedSceneSpace</c> in Core.
/// Matrices are 16 floats laid out column-major (the order <c>THREE.Matrix4.fromArray</c> reads).
/// </para>
///
/// <para>
/// <b>Assets.</b> Meshes and textures are referenced by opaque ids the provider chooses. The host
/// fetches them through <see cref="OpenAsset"/> and never interprets an id, so a provider must
/// validate every id it is handed (the page can send anything). Meshes use the small binary layout
/// described by <see cref="SceneMeshFormat"/>; textures are PNG.
/// </para>
///
/// <para>
/// Calls can be slow the first time (a provider typically reads game archives) and may arrive on
/// any thread, several at once. GUI-only; the CLI ignores this capability.
/// </para>
/// </summary>
public interface ISceneModelProvider
{
    /// <summary>Stable id (kebab-case), unique within the plugin.</summary>
    string Id { get; }

    /// <summary>Short name shown in the 3D view's status line.</summary>
    string Title { get; }

    /// <summary>
    /// True when the provider can currently produce models (for example, the game install it reads
    /// is present). The host checks this before offering game models in the view.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Describes how to draw one placed-object class, keyed by its full class path as the save
    /// stores it (e.g. <c>/Game/Blueprints/.../Deployed_Freezer.Deployed_Freezer_C</c>).
    /// Returns null when the provider has no model for it (the view keeps the box).
    /// </summary>
    SceneClassModel? DescribeClass(string classPath);

    /// <summary>
    /// Describes a class painted in one of the game's paint colours (the save's
    /// <c>EPaintColor</c> value, e.g. 2 for red). Providers that do not know paint return the
    /// unpainted model, which is the default.
    /// </summary>
    SceneClassModel? DescribeClass(string classPath, int paintColor) => DescribeClass(classPath);

    /// <summary>
    /// Describes a class as one saved object looks: painted, and with the crops planted in a
    /// garden plot. Providers that do not know a part of the state ignore it; the default draws the
    /// paint only.
    /// </summary>
    SceneClassModel? DescribeClass(string classPath, SceneObjectState state)
        => state?.PaintColor is { } paint ? DescribeClass(classPath, paint) : DescribeClass(classPath);

    /// <summary>
    /// The static level geometry inside a box of one region, for context around a base. Returns
    /// null when the provider does not draw levels or knows nothing about the region.
    /// </summary>
    SceneLevelSlice? DescribeLevel(SceneLevelQuery query) => null;

    /// <summary>
    /// Opens a mesh or texture by the id a model or slice referenced, or returns null for an
    /// unknown or invalid id.
    /// </summary>
    SceneAsset? OpenAsset(string assetId);
}

/// <summary>How to draw one class: its parts, relative to the object's own origin.</summary>
/// <param name="Parts">Every visible mesh the class draws.</param>
/// <param name="BoundsMin">Local-space bounding box minimum (viewer axes, metres).</param>
/// <param name="BoundsMax">Local-space bounding box maximum (viewer axes, metres).</param>
public sealed record SceneClassModel(IReadOnlyList<ScenePart> Parts, float[] BoundsMin, float[] BoundsMax);

/// <summary>One mesh of a class.</summary>
/// <param name="Mesh">Asset id of the mesh (see <see cref="SceneMeshFormat"/>).</param>
/// <param name="Matrix">Part-to-object transform, 16 floats column-major.</param>
/// <param name="Materials">One entry per material slot the mesh's sections index.</param>
/// <param name="Name">The part's name in the game data, for tooltips.</param>
public sealed record ScenePart(string Mesh, float[] Matrix, IReadOnlyList<SceneMaterial> Materials, string? Name = null);

/// <summary>A simplified material: an optional base colour texture tinted by a colour.</summary>
/// <param name="Texture">Asset id of a PNG base colour texture, or null for flat colour.</param>
/// <param name="Color">Linear RGB tint, three floats 0 to 1.</param>
/// <param name="Opacity">1 for opaque; below 1 draws the material see-through (glass, water).</param>
/// <param name="TwoSided">Draw back faces too.</param>
/// <param name="Masked">The texture's alpha cuts holes (foliage, grilles).</param>
/// <param name="Emissive">Draws unlit at full brightness (lamps, screens).</param>
public sealed record SceneMaterial(
    string? Texture, float[] Color, float Opacity = 1f, bool TwoSided = false, bool Masked = false, bool Emissive = false)
{
    /// <summary>
    /// Terrain only: up to five textures blended per vertex. The mesh's vertex colours (see
    /// <see cref="SceneMeshFormat.FlagVertexColors"/>) hold the weights of layers 2 to 5 in red,
    /// green, blue and alpha; layer 1 takes what is left. Null for an ordinary material.
    /// </summary>
    public IReadOnlyList<SceneTerrainLayer>? Layers { get; init; }

    /// <summary>
    /// The size of one texture repeat in metres when the game maps this material by world position
    /// (water surfaces); 0 when unknown. The view uses it for pieces whose own texture coordinates
    /// would stretch the texture over many metres.
    /// </summary>
    public float WorldTileMetres { get; init; }

    /// <summary>
    /// A decal: drawn over the surface it lies on, see-through where its texture is (the
    /// texture's alpha), never hiding what is behind it.
    /// </summary>
    public bool Decal { get; init; }
}

/// <summary>One texture of a blended terrain material.</summary>
/// <param name="Texture">Asset id of the layer's PNG texture, or null when the slot is unused.</param>
/// <param name="Color">Linear RGB tint, three floats.</param>
/// <param name="RepeatMetres">How many metres one repeat of the texture covers.</param>
public sealed record SceneTerrainLayer(string? Texture, float[] Color, float RepeatMetres);

/// <summary>The saved state of one object that changes how it looks.</summary>
/// <param name="PaintColor">The save's <c>EPaintColor</c> value, or null when unpainted.</param>
/// <param name="Crops">What grows in each spot of a garden plot, or null.</param>
/// <param name="LiquidLevel">How much a liquid container holds, in the save's own units, or null when it is not one.</param>
/// <param name="LiquidType">Which liquid it holds: the save's enum value name (<c>NewEnumerator16</c>), or null.</param>
public sealed record SceneObjectState(int? PaintColor = null, IReadOnlyList<SceneCrop>? Crops = null, int? LiquidLevel = null, string? LiquidType = null);

/// <summary>One planted spot: the spot index, the crop's item row (<c>Plant_Corn</c>) and its growth stage (0 Sprout to 7 Dead).</summary>
public sealed record SceneCrop(int Spot, string Row, int Stage);

/// <summary>A request for the level geometry near a base.</summary>
/// <param name="Region">The save's region name, e.g. <c>Facility_Office1</c> for <c>WorldSave_Facility_Office1.sav</c>.</param>
/// <param name="Min">Viewer-space box minimum (metres).</param>
/// <param name="Max">Viewer-space box maximum (metres).</param>
/// <param name="MaxInstances">Upper bound on drawn instances; the nearest to the box centre win.</param>
/// <param name="ExcludeActors">
/// Level actor names the save already tracks as placed objects (the view draws those itself), so
/// the provider can leave them out instead of drawing them twice.
/// </param>
public sealed record SceneLevelQuery(
    string Region, float[] Min, float[] Max, int MaxInstances, IReadOnlyCollection<string>? ExcludeActors = null);

/// <summary>The level geometry inside a query box, batched by mesh for instanced drawing.</summary>
/// <param name="Batches">One batch per distinct mesh and material set.</param>
/// <param name="TotalInBox">How many instances the box held before <see cref="SceneLevelQuery.MaxInstances"/> applied.</param>
/// <param name="Note">Optional human note (e.g. which level files were read).</param>
/// <param name="PendingMaps">
/// Level files the provider is still reading in the background. When above zero the slice is
/// partial; the view asks again a little later.
/// </param>
public sealed record SceneLevelSlice(IReadOnlyList<SceneLevelBatch> Batches, int TotalInBox, string? Note = null, int PendingMaps = 0);

/// <summary>Many copies of one mesh in the level.</summary>
/// <param name="Mesh">Asset id of the mesh.</param>
/// <param name="Materials">Material per slot.</param>
/// <param name="Matrices">16 floats per instance, column-major, world (viewer) space.</param>
/// <param name="Name">The mesh's name, for tooltips.</param>
public sealed record SceneLevelBatch(string Mesh, IReadOnlyList<SceneMaterial> Materials, float[] Matrices, string? Name = null);

/// <summary>An asset's bytes and media type.</summary>
public sealed record SceneAsset(string ContentType, byte[] Data);

/// <summary>
/// The binary mesh layout (<c>application/x-abiotic-mesh</c>) providers return for mesh assets.
/// Little-endian throughout; every block starts on a 4-byte boundary.
/// <code>
/// "ABM1"                          4 bytes magic
/// uint32 vertexCount
/// uint32 indexCount
/// uint32 sectionCount
/// uint32 flags                    bit 0: 32-bit indices (else 16-bit); bit 1: vertex colours
/// sectionCount x (uint32 materialIndex, uint32 firstIndex, uint32 indexCount)
/// float32[3 x vertexCount]        positions, viewer space, metres
/// int16[3 x vertexCount]          normals, normalized to -32767..32767 (padded to 4 bytes)
/// float32[2 x vertexCount]        texture coordinates
/// uint8[4 x vertexCount]          vertex colours RGBA (only when flag bit 1 is set)
/// uint16|uint32[indexCount]       triangle list, counter-clockwise front faces
/// </code>
/// </summary>
public static class SceneMeshFormat
{
    /// <summary>Media type for <see cref="SceneAsset.ContentType"/>.</summary>
    public const string ContentType = "application/x-abiotic-mesh";

    /// <summary>The four magic bytes, as ASCII.</summary>
    public const string Magic = "ABM1";

    /// <summary>Flag: the index block is 32-bit.</summary>
    public const uint Flag32BitIndices = 1;

    /// <summary>Flag: a block of RGBA8 vertex colours follows the texture coordinates.</summary>
    public const uint FlagVertexColors = 2;

    /// <summary>A triangle-list section drawn with one material.</summary>
    public readonly record struct Section(int MaterialIndex, int FirstIndex, int IndexCount);

    /// <summary>
    /// Encodes a mesh. <paramref name="positions"/> and <paramref name="normals"/> hold three floats
    /// per vertex, <paramref name="uvs"/> two; <paramref name="indices"/> is a triangle list.
    /// </summary>
    public static byte[] Write(
        ReadOnlySpan<float> positions, ReadOnlySpan<float> normals, ReadOnlySpan<float> uvs,
        ReadOnlySpan<uint> indices, IReadOnlyList<Section> sections)
        => Write(positions, normals, uvs, [], indices, sections);

    /// <summary>
    /// Encodes a mesh with vertex colours: <paramref name="colors"/> holds four bytes (RGBA) per
    /// vertex, or is empty for none.
    /// </summary>
    public static byte[] Write(
        ReadOnlySpan<float> positions, ReadOnlySpan<float> normals, ReadOnlySpan<float> uvs, ReadOnlySpan<byte> colors,
        ReadOnlySpan<uint> indices, IReadOnlyList<Section> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var vertexCount = positions.Length / 3;
        if (normals.Length != vertexCount * 3 || uvs.Length != vertexCount * 2)
            throw new ArgumentException("normals and uvs must match the vertex count.");
        if (!colors.IsEmpty && colors.Length != vertexCount * 4)
            throw new ArgumentException("colors must hold four bytes per vertex.");
        var wide = vertexCount > ushort.MaxValue;
        var normalBytes = Align4(vertexCount * 3 * 2);
        var indexBytes = Align4(indices.Length * (wide ? 4 : 2));
        var size = 20 + (sections.Count * 12) + (vertexCount * 12) + normalBytes + (vertexCount * 8) + colors.Length + indexBytes;
        var buffer = new byte[size];
        var w = new SpanWriter(buffer);
        w.Bytes("ABM1"u8);
        w.U32((uint)vertexCount);
        w.U32((uint)indices.Length);
        w.U32((uint)sections.Count);
        w.U32((wide ? Flag32BitIndices : 0) | (colors.IsEmpty ? 0 : FlagVertexColors));
        foreach (var s in sections)
        {
            w.U32((uint)s.MaterialIndex);
            w.U32((uint)s.FirstIndex);
            w.U32((uint)s.IndexCount);
        }
        foreach (var p in positions) w.F32(p);
        foreach (var n in normals) w.I16((short)Math.Clamp(MathF.Round(n * 32767f), -32767f, 32767f));
        w.Pad4();
        foreach (var t in uvs) w.F32(t);
        w.Bytes(colors);
        foreach (var i in indices)
        {
            if (wide) w.U32(i);
            else w.U16((ushort)i);
        }
        w.Pad4();
        return buffer;
    }

    private static int Align4(int n) => (n + 3) & ~3;

    private ref struct SpanWriter(Span<byte> buffer)
    {
        private readonly Span<byte> _buffer = buffer;
        private int _at;

        public void Bytes(ReadOnlySpan<byte> b) { b.CopyTo(_buffer[_at..]); _at += b.Length; }
        public void U32(uint v) { System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_at..], v); _at += 4; }
        public void U16(ushort v) { System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_at..], v); _at += 2; }
        public void I16(short v) { System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(_buffer[_at..], v); _at += 2; }
        public void F32(float v) { System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(_buffer[_at..], v); _at += 4; }
        public void Pad4() { _at = Align4(_at); }
    }
}
