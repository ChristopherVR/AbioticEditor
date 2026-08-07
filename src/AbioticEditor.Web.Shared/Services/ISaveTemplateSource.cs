namespace AbioticEditor.Web.Services;

/// <summary>
/// Supplies the blank save templates a new world is built from
/// (<c>blank-world-template.sav</c>, <c>blank-player-template.sav</c>).
/// </summary>
/// <remarks>
/// The desktop host ships these beside its executable and reads them off disk; the browser host
/// has no disk and fetches them over HTTP from its own static files. Keeping the lookup behind
/// this interface is what lets <see cref="CreateWorldService"/> - and the screens over it - be
/// shared by both, rather than depending on ASP.NET Core's hosting environment.
/// </remarks>
public interface ISaveTemplateSource
{
    /// <summary>
    /// Returns the named template's bytes. Throws <see cref="InvalidOperationException"/> when
    /// the template is missing, which for a correctly installed editor should not happen.
    /// </summary>
    Task<byte[]> ReadTemplateAsync(string name, CancellationToken cancellationToken = default);
}
