using System.Text.Json;
using System.Text.Json.Serialization;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Somewhere to keep what reading a world save produced, so it need not be read again.
/// </summary>
/// <remarks>
/// <para>Reading a world means parsing the whole save. On a desktop that is about a quarter of a
/// second for the ~13 MB facility save and not worth a thought; the same work in a browser takes
/// over five seconds, on the one thread the page draws with, because WebAssembly is roughly
/// twenty times slower at it. That cost is unavoidable the first time. Paying it again on every
/// visit is not.</para>
///
/// <para>Only the browser implements this. The desktop registers a do-nothing one: caching a
/// quarter-second parse to disk would cost more than it saved.</para>
/// </remarks>
public interface IWorldFactsCache
{
    /// <summary>What was stored under <paramref name="key"/>, or null when nothing was.</summary>
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stores <paramref name="json"/> under <paramref name="key"/>, replacing any earlier value.</summary>
    Task WriteAsync(string key, string json, CancellationToken cancellationToken = default);
}

/// <summary>A cache that keeps nothing, for hosts fast enough not to need one.</summary>
public sealed class NoWorldFactsCache : IWorldFactsCache
{
    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public Task WriteAsync(string key, string json, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// The parts of a world save the editor keeps between visits.
/// </summary>
/// <remarks>
/// A hand-written shape rather than the domain records themselves. <see cref="WorldDeployable"/>
/// carries a dozen computed properties (its display name, whether it is a bed, who claimed it)
/// which would all be written out and none of which can be read back, so storing it directly
/// would bloat the file and quietly depend on what happens to be computed today. These are the
/// fields the world actually holds.
/// </remarks>
public sealed record CachedWorldFacts(
    IReadOnlyList<CachedDeployable> Deployables,
    IReadOnlyList<string> Flags)
{
    /// <summary>Bumped whenever the shape below changes, so old entries are ignored rather than misread.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public static CachedWorldFacts From(IReadOnlyList<WorldDeployable> deployables, IEnumerable<string> flags)
        => new(
            deployables.Select(d => new CachedDeployable(
                d.Id, d.ClassName, d.X, d.Y, d.Z, d.HasInventory, d.StoredItemCount, d.CustomName,
                d.InstalledUpgrades.Count == 0 ? null : [.. d.InstalledUpgrades])).ToArray(),
            [.. flags]);

    public IReadOnlyList<WorldDeployable> ToDeployables()
        => Deployables
            .Select(d => new WorldDeployable(
                d.Id, d.ClassName, d.X, d.Y, d.Z, d.HasInventory, d.StoredItemCount, d.CustomName, d.Upgrades))
            .ToArray();
}

/// <inheritdoc cref="CachedWorldFacts"/>
public sealed record CachedDeployable(
    string Id,
    string? ClassName,
    double X,
    double Y,
    double Z,
    bool HasInventory,
    int StoredItemCount,
    string? CustomName,
    IReadOnlyList<string>? Upgrades);

/// <summary>
/// Source-generated serialization for the cache.
/// </summary>
/// <remarks>
/// Generated rather than reflection-based because the browser build publishes fully trimmed:
/// a reflecting serializer would find its types stripped and fail at run time, on a code path
/// that only runs on someone else's machine.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CachedWorldFacts))]
internal sealed partial class WorldFactsJsonContext : JsonSerializerContext;

/// <summary>Reads and writes <see cref="CachedWorldFacts"/> as JSON, tolerating anything unusable.</summary>
internal static class WorldFactsJson
{
    public static string Write(CachedWorldFacts facts)
        => JsonSerializer.Serialize(facts, WorldFactsJsonContext.Default.CachedWorldFacts);

    /// <summary>Null when the text is absent, corrupt, or from an older shape than this build reads.</summary>
    public static CachedWorldFacts? Read(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var facts = JsonSerializer.Deserialize(json, WorldFactsJsonContext.Default.CachedWorldFacts);
            return facts?.Version == CachedWorldFacts.CurrentVersion ? facts : null;
        }
        catch (JsonException)
        {
            // A half-written or outdated entry is not worth an error - the world is simply read again.
            return null;
        }
    }
}
