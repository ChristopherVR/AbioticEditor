using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>Creates a loose-file Steam world from the same blank templates as the desktop host.</summary>
public sealed class CreateWorldService
{
    private readonly ISaveTemplateSource _templates;

    public CreateWorldService(ISaveTemplateSource templates) => _templates = templates;

    public async Task<string> CreateSteamWorldAsync(CreateSteamWorldRequest request, CancellationToken cancellationToken = default)
        => await CreateWorldAsync(new CreateWorldRequest(request.WorldName, request.ParentDirectory, [request.SteamId], request.GameDifficulty), cancellationToken);

    /// <summary>Creates a loose-file Steam/Proton world with one or more explicit player identities.</summary>
    public async Task<string> CreateWorldAsync(CreateWorldRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var worldName = ValidateWorldName(request.WorldName);
        var parentDirectory = ValidateParentDirectory(request.ParentDirectory);
        var playerIds = request.PlayerIds.Select(ValidatePlayerId).Distinct(StringComparer.Ordinal).ToArray();
        if (playerIds.Length == 0) throw new ArgumentException("Add at least one player id.", nameof(request));
        if (request.GameDifficulty is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(request), "Choose a difficulty between 1 and 4.");

        var metadataTemplate = await ReadTemplateAsync("blank-world-template.sav", cancellationToken);
        var playerTemplate = await ReadTemplateAsync("blank-player-template.sav", cancellationToken);
        return await Task.Run(() => WorldSaveFactory.CreateWorldFolder(new CreateWorldOptions
        {
            WorldName = worldName,
            ParentDirectory = parentDirectory,
            PlayerIds = playerIds,
            GameDifficulty = request.GameDifficulty,
        }, metadataTemplate, playerTemplate), cancellationToken);
    }

    /// <summary>Adds a blank player or a re-homed copy to an existing loose-file world.</summary>
    public async Task<string> AddPlayerAsync(AddWorldPlayerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var worldFolder = Path.GetFullPath(request.WorldFolder);
        if (!Directory.Exists(worldFolder)) throw new DirectoryNotFoundException($"The world folder does not exist: {worldFolder}");
        var playerId = ValidatePlayerId(request.PlayerId);
        var playerDirectory = request.CopySourcePath is { Length: > 0 }
            ? Path.GetDirectoryName(Path.GetFullPath(request.CopySourcePath))!
            : Path.Combine(worldFolder, "PlayerData");
        Directory.CreateDirectory(playerDirectory);
        if (!string.IsNullOrWhiteSpace(request.CopySourcePath))
            return await Task.Run(() => AbioticEditor.Core.PlayerSaves.PlayerSaveIdentity.CloneToNewId(request.CopySourcePath, playerId), cancellationToken);
        var template = await ReadTemplateAsync("blank-player-template.sav", cancellationToken);
        return await Task.Run(() => AbioticEditor.Core.PlayerSaves.PlayerSaveFactory.CreateFromTemplate(template, playerDirectory, playerId), cancellationToken);
    }

    private Task<byte[]> ReadTemplateAsync(string name, CancellationToken cancellationToken)
        => _templates.ReadTemplateAsync(name, cancellationToken);

    private static string ValidateWorldName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A world name is required.");
        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("World name must be a single valid folder name.");
        return name;
    }

    private static string ValidateParentDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A save location is required.");
        var path = Path.GetFullPath(value.Trim());
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"The save location does not exist: {path}");
        return path;
    }

    /// <summary>
    /// Validates a player id as a safe <c>Player_&lt;id&gt;.sav</c> file-name component. Deliberately
    /// not numeric-only: Steam ids are always a 17-digit SteamID64, but a Game Pass / Xbox account
    /// id or another platform's custom token only needs to be filename-safe (see
    /// <see cref="PlayerIdentifier.IsSafeFileToken"/>, the same rule the desktop host and the
    /// player-rename flow use).
    /// </summary>
    private static string ValidatePlayerId(string? value)
    {
        var id = value?.Trim() ?? string.Empty;
        if (!PlayerIdentifier.IsSafeFileToken(id))
            throw new ArgumentException("Enter a valid player id (letters, digits, '-', '_' or '.' only).");
        return id;
    }
}

public sealed record CreateSteamWorldRequest(string WorldName, string ParentDirectory, string SteamId, int GameDifficulty);
public sealed record CreateWorldRequest(string WorldName, string ParentDirectory, IReadOnlyList<string> PlayerIds, int GameDifficulty);
public sealed record AddWorldPlayerRequest(string WorldFolder, string PlayerId, string? CopySourcePath = null);
