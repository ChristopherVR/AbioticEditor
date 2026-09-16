using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

/// <summary>Appearance of the current running character, loaded when its panel opens.</summary>
public interface IPlayerAppearanceSession
{
    IReadOnlyList<CustomizationField> Fields { get; }
    bool CanEdit { get; }
    bool CanSaveProfile { get; }
    bool HasProfileChanges { get; }
    Task SaveProfileAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task SetAsync(string propertyName, string rowName, CancellationToken cancellationToken = default);
}
