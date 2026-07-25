using AbioticEditor.Core.GamePass;
using AbioticEditor.Web.Models;
using AbioticEditor.Web.Services;

namespace AbioticEditor.Tests;

public sealed class CustomizationSaveSessionTests
{
    [Fact]
    public void Appearance_edits_are_revertible_and_save_with_backup()
    {
        Assert.NotNull(Fixtures.ClientSavedDir);
        var source = Directory.EnumerateFiles(Fixtures.ClientSavedDir!, "ScientistCustomization_*.sav", SearchOption.AllDirectories).First();
        var directory = Directory.CreateTempSubdirectory("abiotic-appearance-");
        var path = Path.Combine(directory.FullName, Path.GetFileName(source));
        File.Copy(source, path);
        try
        {
            var session = CustomizationSaveSession.Load(path);
            Assert.NotEmpty(session.Fields);
            var original = session.Fields[0].Value;
            session.Fields[0].Value = original + "_Test";
            Assert.True(session.IsDirty);
            session.Revert();
            Assert.Equal(original, session.Fields[0].Value);
            Assert.False(session.IsDirty);

            session.Fields[0].Value = original + "_Test";
            session.Save();
            Assert.True(File.Exists(path + ".bak"));
            Assert.False(session.IsDirty);
            Assert.Equal(original + "_Test", CustomizationSaveSession.Load(path).Fields[0].Value);
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Appearance_discovery_uses_the_player_account_directory()
    {
        Assert.NotNull(Fixtures.ClientSavedDir);
        const string account = "76561197993781479";
        var player = Directory.EnumerateFiles(Path.Combine(Fixtures.ClientSavedDir!, account), $"Player_{account}.sav", SearchOption.AllDirectories).First();
        var discovered = CustomizationSaveSession.DiscoverNearPlayer(player, account);
        Assert.Contains(discovered, path => Path.GetFileName(path) == "ScientistCustomization_1.sav");
    }

    [Fact]
    public void GamePass_appearance_edits_write_back_into_the_profile_container_with_a_backup()
    {
        // The wgs fixture has no ProfileScientistCustomization container, so build one in a
        // scratch wgs folder from the Steam appearance fixture (same GVAS payload, different
        // packaging) using the same Core container writer the Steam-to-Game-Pass converter uses.
        Assert.NotNull(Fixtures.ClientSavedDir);
        var source = Directory.EnumerateFiles(Fixtures.ClientSavedDir!, "ScientistCustomization_*.sav", SearchOption.AllDirectories).First();
        var scratch = Directory.CreateTempSubdirectory("abiotic-gp-appearance-");
        var wgs = Path.Combine(scratch.FullName, "wgs");
        try
        {
            WgsContainerStore.WriteNewContainer(wgs, "ProfileScientistCustomization_1", File.ReadAllBytes(source));

            var set = GamePassSaveSet.Open(wgs);
            Assert.Equal([1], set.CustomizationSlots());

            var session = CustomizationSaveSession.LoadGamePass(set, 1);
            Assert.NotNull(session);
            Assert.True(session!.IsGamePass);
            Assert.NotEmpty(session.Fields);
            var original = session.Fields[0].Value;
            session.Fields[0].Value = original + "_Test";
            Assert.True(session.IsDirty);
            session.Save();
            Assert.False(session.IsDirty);
            Assert.True(Directory.Exists(wgs + ".bak"), "the first container write must back up the whole wgs folder");

            var reloaded = CustomizationSaveSession.LoadGamePass(GamePassSaveSet.Open(wgs), 1);
            Assert.Equal(original + "_Test", reloaded!.Fields[0].Value);
        }
        finally { scratch.Delete(recursive: true); }
    }

    [Fact]
    public void GamePass_store_is_located_beside_a_converted_Steam_copy()
    {
        if (Fixtures.GamePassWgsDir is not { } fixture) return; // fixture absent - skip
        // Game Pass bundles are Oodle-compressed and the only Oodle build the editor can bind
        // to is the game's Windows DLL, so this cannot run on Linux or macOS CI.
        if (!OodleCodec.IsAvailable) return;
        var scratch = Directory.CreateTempSubdirectory("abiotic-gp-locate-");
        var wgs = Path.Combine(scratch.FullName, "Fixture");
        try
        {
            CopyDirectory(fixture, wgs);
            var converted = SaveConversionService.Convert(SaveConversionDirection.ToSteam, wgs, playerAccountId: null);

            // The loose copy the workspace opens sits beside the container folder as
            // "<wgs>-Steam"; appearance editing must find its way back to the containers.
            Assert.Equal(wgs, CustomizationSaveSession.TryLocateGamePassStore(converted));

            // This fixture ships no profile customization container: the editor must land in
            // the honest "not found in your Game Pass save data" state, not a silent no-op.
            Assert.Empty(GamePassSaveSet.Open(wgs).CustomizationSlots());

            // A plain Steam world folder never resolves to a wgs store.
            if (Fixtures.CascadeDir is not null)
                Assert.Null(CustomizationSaveSession.TryLocateGamePassStore(Fixtures.CascadeDir));
        }
        finally { scratch.Delete(recursive: true); }
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
