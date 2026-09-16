using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.Web.Models;

public sealed class LivePlayerAppearanceSession(LivePlayerAppearanceChannel channel, Func<string?> playerId) : IPlayerAppearanceSession
{
    public IReadOnlyList<CustomizationField> Fields { get; private set; } = [];
    public bool CanEdit { get; private set; }
    public bool CanSaveProfile { get; private set; }
    public bool HasProfileChanges { get; private set; }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var result = await channel.GetAsync(playerId(), cancellationToken).ConfigureAwait(false);
        Fields = CustomizationSaveFile.KnownFields.Where(field => result.Fields?.ContainsKey(field.PropertyName) == true)
            .Select(field => new CustomizationField(field.PropertyName, field.Label, field.TableName, result.Fields![field.PropertyName])).ToArray();
        CanEdit = result.CanEdit;
        CanSaveProfile = result.CanSaveProfile;
        HasProfileChanges = result.HasProfileChanges;
    }
    public async Task SaveProfileAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSaveProfile) throw new InvalidOperationException("Only this computer's local character profile can be saved.");
        await channel.SaveProfileAsync(playerId(), cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
    public async Task SetAsync(string propertyName, string rowName, CancellationToken cancellationToken = default)
    {
        if (!CanEdit) throw new InvalidOperationException("Appearance editing requires an updated host agent.");
        if (!Fields.Any(field => field.PropertyName == propertyName)) throw new ArgumentException("Unknown appearance field.", nameof(propertyName));
        await channel.SetAsync(propertyName, rowName, playerId(), cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }
}
