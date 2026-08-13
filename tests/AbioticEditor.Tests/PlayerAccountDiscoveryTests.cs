using AbioticEditor.Core.GamePass;
using AbioticEditor.Core.Saves;

namespace AbioticEditor.Tests;

/// <summary>
/// <see cref="PlayerAccountDiscovery"/>: the accounts offered as a shortcut wherever the editor
/// needs a player account. The merge is tested through <see cref="PlayerAccountDiscovery.Gather"/>
/// so the rules hold on a machine with no game and no Steam client installed.
/// </summary>
public sealed class PlayerAccountDiscoveryTests
{
    private const string SteamId = "76561197993781479";
    private const string OtherSteamId = "76561198000000000";

    private static DiscoveredWorld World(string? accountId, SavePlatform platform, string name = "Cascade")
        => new($"C:/saves/{name}", name, DiscoveredWorldSource.Client, accountId) { Platform = platform };

    [Fact]
    public void Gather_returns_nothing_when_this_machine_has_nothing_on_it()
    {
        var accounts = PlayerAccountDiscovery.Gather(
            Array.Empty<DiscoveredWorld>(), Array.Empty<DiscoveredGamePassSave>(), new Dictionary<string, string>());

        Assert.Empty(accounts);
    }

    [Fact]
    public void Gather_tolerates_every_source_being_absent()
    {
        // A machine with no game at all reaches this with three empty scans; nulls are accepted
        // for the same reason, so a caller that skipped a scan cannot take the list down.
        Assert.Empty(PlayerAccountDiscovery.Gather(null, null, null));
    }

    [Fact]
    public void Gather_names_a_Steam_account_from_the_personas_this_machine_has_signed_into()
    {
        var accounts = PlayerAccountDiscovery.Gather(
            [World(SteamId, SavePlatform.Steam)],
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string> { [SteamId] = "Tribbes" });

        var account = Assert.Single(accounts);
        Assert.Equal(SteamId, account.AccountId);
        Assert.Equal(SavePlatform.Steam, account.Platform);
        Assert.Equal("Tribbes", account.DisplayName);
    }

    [Fact]
    public void Gather_keeps_an_account_with_no_name_rather_than_dropping_it()
    {
        // A world whose owner has never signed into Steam on this machine still names an account
        // worth offering; there is simply nothing friendlier to call it than its own id.
        var accounts = PlayerAccountDiscovery.Gather(
            [World(SteamId, SavePlatform.Steam)],
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string>());

        var account = Assert.Single(accounts);
        Assert.Equal(SteamId, account.AccountId);
        Assert.Null(account.DisplayName);
    }

    [Fact]
    public void Gather_offers_a_signed_in_Steam_account_that_has_never_played()
    {
        // The usual reason to want an account at all: handing a world to an account with no
        // Abiotic Factor save of its own yet.
        var accounts = PlayerAccountDiscovery.Gather(
            Array.Empty<DiscoveredWorld>(),
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string> { [OtherSteamId] = "Vasya" });

        var account = Assert.Single(accounts);
        Assert.Equal(OtherSteamId, account.AccountId);
        Assert.Equal(SavePlatform.Steam, account.Platform);
        Assert.Equal("Vasya", account.DisplayName);
    }

    [Fact]
    public void Gather_lists_an_Xbox_account_as_a_Game_Pass_one()
    {
        var accounts = PlayerAccountDiscovery.Gather(
            Array.Empty<DiscoveredWorld>(),
            [new DiscoveredGamePassSave("C:/wgs/2535A_1234", "2535A")],
            new Dictionary<string, string>());

        var account = Assert.Single(accounts);
        Assert.Equal("2535A", account.AccountId);
        Assert.Equal(SavePlatform.GamePass, account.Platform);
        Assert.Null(account.DisplayName);
    }

    [Fact]
    public void Gather_lists_one_account_once_however_many_scans_found_it()
    {
        // The same Steam account owns three worlds and is signed into the Steam client: four
        // sightings, one entry, and the persona name still lands on it.
        var accounts = PlayerAccountDiscovery.Gather(
            [
                World(SteamId, SavePlatform.Steam, "Cascade"),
                World(SteamId, SavePlatform.Steam, "Reactors"),
                World(SteamId, SavePlatform.Steam, "Labs"),
            ],
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string> { [SteamId] = "Tribbes" });

        var account = Assert.Single(accounts);
        Assert.Equal("Tribbes", account.DisplayName);
    }

    [Fact]
    public void Gather_matches_an_Xbox_account_written_in_either_case()
    {
        var accounts = PlayerAccountDiscovery.Gather(
            [World("2535abcd", SavePlatform.GamePass)],
            [new DiscoveredGamePassSave("C:/wgs/2535ABCD_1", "2535ABCD")],
            new Dictionary<string, string>());

        Assert.Single(accounts);
    }

    [Fact]
    public void Gather_calls_a_Steam_shaped_account_a_Steam_one_even_when_it_arrived_unlabelled()
    {
        // A world found under a plain folder drop carries no platform. The id still says what it is.
        var accounts = PlayerAccountDiscovery.Gather(
            [World(SteamId, SavePlatform.Unknown)],
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string>());

        Assert.Equal(SavePlatform.Steam, Assert.Single(accounts).Platform);
    }

    [Fact]
    public void Gather_leaves_a_genuinely_unknown_account_unlabelled()
    {
        var accounts = PlayerAccountDiscovery.Gather(
            [World("epic-player-1", SavePlatform.Unknown)],
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string>());

        Assert.Equal(SavePlatform.Unknown, Assert.Single(accounts).Platform);
    }

    [Fact]
    public void Gather_does_not_let_a_later_scan_forget_what_an_earlier_one_knew()
    {
        // The Game Pass scan knows nothing but the id. It must not overwrite the platform or the
        // name an earlier pass established for the same account.
        var accounts = PlayerAccountDiscovery.Gather(
            [World(SteamId, SavePlatform.Steam)],
            [new DiscoveredGamePassSave("C:/wgs/x", SteamId)],
            new Dictionary<string, string> { [SteamId] = "Tribbes" });

        var account = Assert.Single(accounts);
        Assert.Equal(SavePlatform.Steam, account.Platform);
        Assert.Equal("Tribbes", account.DisplayName);
    }

    [Fact]
    public void Gather_skips_the_worlds_that_have_no_owning_account()
    {
        // Dedicated-server worlds keep their saves in the install folder, with no account folder
        // above them, so they name nobody.
        var accounts = PlayerAccountDiscovery.Gather(
            [
                new DiscoveredWorld("C:/server/Cascade", "Cascade", DiscoveredWorldSource.DedicatedServer, null),
                World("   ", SavePlatform.Unknown),
            ],
            Array.Empty<DiscoveredGamePassSave>(),
            new Dictionary<string, string>());

        Assert.Empty(accounts);
    }

    [Fact]
    public void Gather_puts_Steam_first_then_Game_Pass_then_the_rest_and_sorts_by_what_is_shown()
    {
        var accounts = PlayerAccountDiscovery.Gather(
            [
                World("epic-player-1", SavePlatform.Unknown),
                World(SteamId, SavePlatform.Steam),
                World(OtherSteamId, SavePlatform.Steam),
            ],
            [new DiscoveredGamePassSave("C:/wgs/x", "2535A")],
            new Dictionary<string, string> { [SteamId] = "Zoe", [OtherSteamId] = "Alex" });

        Assert.Equal(
            [OtherSteamId, SteamId, "2535A", "epic-player-1"],
            accounts.Select(account => account.AccountId));

        // Alex before Zoe: the sort follows the name on the chip, not the id behind it.
        Assert.Equal(["Alex", "Zoe", null, null], accounts.Select(account => account.DisplayName));
    }

    [Fact]
    public void DiscoverAll_scans_this_machine_without_throwing()
    {
        // Whatever is installed here, the scan has to come back with an answer: every account the
        // list offers is a shortcut, and none of them is required for a conversion to work.
        var accounts = PlayerAccountDiscovery.DiscoverAll();

        Assert.NotNull(accounts);
        Assert.All(accounts, account => Assert.False(string.IsNullOrWhiteSpace(account.AccountId)));
        Assert.Equal(
            accounts.Select(account => account.AccountId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            accounts.Count);
    }
}
