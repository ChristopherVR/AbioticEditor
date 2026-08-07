using AbioticEditor.Core.Assets;
using AbioticEditor.Core.Codex;

namespace AbioticEditor.Web.Services;

/// <summary>
/// Provides the trader roster to UI sessions, preferring live stock read from the installed
/// game's paks (the retired native app's behavior) and falling back to the built-in
/// <see cref="TraderCatalog.Fallback"/> snapshot when the game isn't installed or the tables
/// can't be read. The result is cached because mounting paks is expensive.
/// </summary>
public sealed class TraderVocabularyService
{
    private Lazy<IReadOnlyList<TraderInfo>> _traders = new(Load);

    public IReadOnlyList<TraderInfo> GetTraders() => _traders.Value;

    public void Reload() => Interlocked.Exchange(ref _traders, new Lazy<IReadOnlyList<TraderInfo>>(Load));

    private static IReadOnlyList<TraderInfo> Load()
    {
        try
        {
            using var provider = GameDataGate.CreateProvider();
            // LoadFrom itself falls back to the snapshot when the tables are unreadable.
            return provider is null ? TraderCatalog.Fallback : TraderCatalog.LoadFrom(provider);
        }
        catch
        {
            return TraderCatalog.Fallback;
        }
    }
}
