using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.Saves;
using AbioticEditor.Web.Services;
using AbioticEditor.Ui;

namespace AbioticEditor.Tests;

public sealed class SaveConversionServiceTests
{
    [SkippableFact]
    public void Recognises_real_Steam_and_GamePass_world_fixtures()
    {
        var steam = Fixtures.CascadeDir;
        var gamePass = Fixtures.GamePassWgsDir;
        Skip.IfNot(steam is not null && gamePass is not null,
            "the Steam and Game Pass world fixtures are not both in this checkout");

        Assert.Equal(SaveConversionSourceValidation.Valid,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToGamePass, steam));
        Assert.Equal(SaveConversionSourceValidation.Valid,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToSteam, gamePass));
        Assert.Equal(SaveConversionSourceValidation.MissingGamePassContainer,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToSteam, steam));
        Assert.Equal(SaveConversionSourceValidation.MissingSteamWorldSave,
            SaveConversionService.ValidateSource(SaveConversionDirection.ToGamePass, gamePass));
    }

    [Theory]
    // Blank is the normal answer: it means leave the existing accounts alone.
    [InlineData(SaveConversionDirection.ToSteam, "", SaveConversionIdWarning.None)]
    [InlineData(SaveConversionDirection.ToSteam, "   ", SaveConversionIdWarning.None)]
    [InlineData(SaveConversionDirection.ToSteam, null, SaveConversionIdWarning.None)]
    // A real 17-digit SteamID64 heading for Steam, with and without the spaces a paste brings.
    [InlineData(SaveConversionDirection.ToSteam, "76561197993781479", SaveConversionIdWarning.None)]
    [InlineData(SaveConversionDirection.ToSteam, "  76561197993781479  ", SaveConversionIdWarning.None)]
    // Allowed, but worth a second look: these cannot be Steam accounts.
    [InlineData(SaveConversionDirection.ToSteam, "7656119799378147", SaveConversionIdWarning.NotShapedLikeASteamId)]
    [InlineData(SaveConversionDirection.ToSteam, "765611979937814790", SaveConversionIdWarning.NotShapedLikeASteamId)]
    [InlineData(SaveConversionDirection.ToSteam, "2535A1B2C3D4", SaveConversionIdWarning.NotShapedLikeASteamId)]
    // Nothing honest to say about an Xbox account: they are opaque, so any safe token passes.
    [InlineData(SaveConversionDirection.ToGamePass, "2535A1B2C3D4", SaveConversionIdWarning.None)]
    [InlineData(SaveConversionDirection.ToGamePass, "an-account-nobody-here-has-heard-of", SaveConversionIdWarning.None)]
    [InlineData(SaveConversionDirection.ToGamePass, "76561197993781479", SaveConversionIdWarning.None)]
    // A save is named after its account, so these are turned down by the conversion itself.
    [InlineData(SaveConversionDirection.ToSteam, "../escape", SaveConversionIdWarning.UnusableInFileName)]
    [InlineData(SaveConversionDirection.ToGamePass, "C:\\somewhere", SaveConversionIdWarning.UnusableInFileName)]
    [InlineData(SaveConversionDirection.ToGamePass, "has a space", SaveConversionIdWarning.UnusableInFileName)]
    [InlineData(SaveConversionDirection.ToGamePass, "CON", SaveConversionIdWarning.UnusableInFileName)]
    [InlineData(SaveConversionDirection.ToSteam, "..", SaveConversionIdWarning.UnusableInFileName)]
    public void Warns_about_a_doubtful_account_without_refusing_it(
        SaveConversionDirection direction, string? accountId, SaveConversionIdWarning expected)
        => Assert.Equal(expected, SaveConversionService.WarnAboutAccountId(direction, accountId));

    [Fact]
    public void An_account_this_machine_has_never_seen_is_allowed_through()
    {
        // Converting to an account that has not played yet is the ordinary reason to type one at
        // all, so an id no scan on this machine knows must raise nothing firmer than a nudge.
        Assert.Equal(
            SaveConversionIdWarning.None,
            SaveConversionService.WarnAboutAccountId(SaveConversionDirection.ToSteam, "76561198000000000"));
        Assert.Equal(
            SaveConversionIdWarning.None,
            SaveConversionService.WarnAboutAccountId(SaveConversionDirection.ToGamePass, "brand.new_account-1"));
    }

    private const string SteamCharacter = "76561197993781479";
    private const string XboxCharacter = "2533274900397709";

    [Fact]
    public void Warns_that_an_Xbox_character_will_not_be_reachable_on_Steam()
    {
        // The reported bug: the world converts, the character comes across whole, and the game
        // starts a new level 1 one because it is looking for a save named after the Steam account.
        Assert.True(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, [XboxCharacter], playerAccountId: null));
    }

    [Theory]
    [InlineData("76561197993781479")]
    [InlineData("  76561197993781479  ")]
    public void Says_nothing_once_an_account_has_been_given_to_re_home_to(string accountId)
    {
        // Re-homing is exactly the fix, so the warning has to go away the moment it is set up.
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, [XboxCharacter], accountId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Treats_a_blank_account_as_no_account_at_all(string? accountId)
        => Assert.True(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, [XboxCharacter], accountId));

    [Fact]
    public void Says_nothing_about_a_world_with_no_characters_in_it()
    {
        // A world nobody has played has nothing to strand, either way round.
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, [], playerAccountId: null));
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToGamePass, [], playerAccountId: null));
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, null, playerAccountId: null));
    }

    [Fact]
    public void Says_nothing_when_a_character_already_suits_where_it_is_going()
    {
        // Converted back and forth: the character is already on a SteamID64, so heading to Steam
        // it lands exactly where the game will look for it.
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, [SteamCharacter], playerAccountId: null));

        // And an Xbox-shaped account heading to Game Pass might well be the player's own, so
        // there is nothing certain enough to warn about.
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToGamePass, [XboxCharacter], playerAccountId: null));
    }

    [Fact]
    public void Warns_that_a_Steam_character_will_not_be_reachable_on_Game_Pass()
    {
        // A SteamID64 is never an Xbox account, so this direction has the same silent failure.
        Assert.True(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToGamePass, [SteamCharacter], playerAccountId: null));
    }

    [Fact]
    public void Leaves_a_shared_world_alone_when_one_of_its_characters_already_fits()
    {
        // A co-op world where somebody has already been re-homed: the player has a character the
        // game will find, so this is not the silent failure the warning is for.
        Assert.False(SaveConversionService.WouldStrandCharacters(
            SaveConversionDirection.ToSteam, [XboxCharacter, SteamCharacter], playerAccountId: null));
    }

    [Theory]
    [InlineData(SaveConversionDirection.ToSteam, SavePlatform.Steam)]
    [InlineData(SaveConversionDirection.ToGamePass, SavePlatform.GamePass)]
    public void Knows_which_platform_each_direction_ends_on(
        SaveConversionDirection direction, SavePlatform expected)
        => Assert.Equal(expected, SaveConversionService.DestinationPlatform(direction));

    [Fact]
    public void Writes_a_GamePass_conversion_beside_the_selected_Steam_folder()
    {
        // A Steam world folder already lives somewhere normal, so the Game Pass copy goes right
        // beside it.
        var source = Path.Combine(Path.GetTempPath(), "AbioticEditor conversion source");
        Assert.Equal(
            Path.GetFullPath(source) + "-GamePass",
            SaveConversionService.DestinationFor(SaveConversionDirection.ToGamePass, source));
    }

    [Fact]
    public void Writes_a_Steam_conversion_to_the_normal_save_location_not_inside_the_Xbox_package_folder()
    {
        // The reported bug: a Game Pass source lives inside the Xbox app's own virtualized
        // package folder, and writing "beside it" buried the converted Steam world in there too -
        // nowhere the game, or the player, would ever look for a Steam save.
        var source = Path.Combine(
            Path.GetTempPath(), "Packages", "PlayStack.AbioticFactor_3wcqaesafpzy",
            "SystemAppData", "wgs", "0009_00000000");
        var destination = SaveConversionService.DestinationFor(SaveConversionDirection.ToSteam, source, "MyWorld-WC");

        Assert.Equal("MyWorld", Path.GetFileName(destination));
        Assert.Contains(Path.Combine("AbioticFactor", "Saved", "SaveGames"), destination);
        Assert.DoesNotContain("Packages", destination, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wgs", destination, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void Converts_the_GamePass_fixture_through_the_settings_service()
    {
        Skip.IfNot(Fixtures.GamePassWgsDir is not null, "the Game Pass fixture is not in this checkout");
        var fixture = Fixtures.GamePassWgsDir!;
        // Game Pass bundles are Oodle-compressed and the only Oodle build the editor can bind
        // to is the game's Windows DLL, so this cannot run on Linux or macOS CI.
        Skip.IfNot(OodleCodec.IsAvailable, "no native Oodle library on this machine, so a Game Pass bundle cannot be unpacked");
        var root = Path.Combine(Path.GetTempPath(), "AbioticEditorTests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Fixture");
        string? output = null;
        try
        {
            CopyDirectory(fixture, source);
            output = SaveConversionService.Convert(SaveConversionDirection.ToSteam, source, playerAccountId: null);

            // The destination is the normal Steam save location, not beside the source - see
            // SaveConversionServiceTests.Writes_a_Steam_conversion_to_the_normal_save_location...
            // for why writing beside the source was wrong for this direction.
            Assert.Contains(Path.Combine("AbioticFactor", "Saved", "SaveGames"), output);
            Assert.NotEmpty(Directory.EnumerateFiles(output, "WorldSave_*.sav", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            // Unlike the source, the output does not live under root - it is written to the real
            // Steam save location, same as the app would for a genuine conversion - so it needs
            // its own cleanup or every run leaves another test world behind on the machine.
            if (output is not null && Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    [SkippableFact]
    public async Task Opening_logs_creates_the_folder_before_revealing_it()
    {
        var path = Path.Combine(Path.GetTempPath(), "AbioticEditorTests", Guid.NewGuid().ToString("N"), "logs");
        var navigation = new RecordingNavigation();
        try
        {
            await LogFolderOpener.OpenAsync(navigation, path);

            Assert.True(Directory.Exists(path));
            Assert.Equal(path, navigation.RevealedPath);
        }
        finally
        {
            var root = Directory.GetParent(path)!.FullName;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingNavigation : IExternalNavigationService
    {
        public string? RevealedPath { get; private set; }
        public Task OpenUrlAsync(Uri url, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RevealPathAsync(string path, CancellationToken cancellationToken = default)
        {
            Assert.True(Directory.Exists(path));
            RevealedPath = path;
            return Task.CompletedTask;
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
