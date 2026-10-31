using System.IO;
using System.Text.Json;
using AbioticEditor.Core.LiveEditing;
using AbioticEditor.Core.LiveEditing.Player;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Web.Models;

namespace AbioticEditor.Tests;

/// <summary>
/// The Vitals tab's effects, radiation and temperature through both sessions: the file session
/// stages them for SAVE, the live session sends the agent's effects/toggles/vitals commands.
/// </summary>
public class PlayerEffectsSessionTests
{
    private static readonly BuffInfo Caffeinated = new("Buff_Caffeinated", "Caffeinated", "", 300, false, true);
    private static readonly BuffInfo Stinky = new("Debuff_Stinky", "Stinky", "", 0, true, true);

    private static (PlayerSaveSession Session, string Path, DirectoryInfo Directory) OpenCopy(string file)
    {
        var directory = Directory.CreateTempSubdirectory("effects-session-");
        var path = Path.Combine(directory.FullName, file);
        File.Copy(Path.Combine(Fixtures.CascadeDir!, "PlayerData", file), path);
        return (new PlayerSaveSession(PlayerSaveReader.ReadFromFile(path), path), path, directory);
    }

    [SkippableFact]
    public async Task File_session_adds_effects_with_their_duration_and_clears_only_debuffs()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var (session, path, directory) = OpenCopy("Player_76561197993781479.sav");
        try
        {
            var effects = session;
            Assert.False(effects.AppliesImmediately);
            Assert.True(effects.CanAddEffects);
            Assert.Empty(effects.Effects);

            await effects.AddEffectAsync(Caffeinated);
            await effects.AddEffectAsync(Stinky);
            Assert.True(session.IsDirty);
            Assert.Equal(300, effects.Effects[0].SecondsLeft);
            Assert.True(effects.Effects[1].Permanent);

            session.Vitals.Radiation = 33;
            session.Vitals.BodyTemperature = -1.5;
            await session.SaveAsync();

            var saved = PlayerSaveReader.ReadFromFile(path);
            Assert.Equal([new ActiveBuff("Buff_Caffeinated", "EBodyLimbs::AllBones", 300), new ActiveBuff("Debuff_Stinky", "EBodyLimbs::AllBones", -1)], saved.ActiveBuffs);
            Assert.Equal(33, saved.Stats.Radiation);
            Assert.Equal(-1.5, saved.Stats.BodyTemperature);

            var reopened = new PlayerSaveSession(saved, path);
            await reopened.ClearDebuffsAsync();
            Assert.Equal(["Buff_Caffeinated"], reopened.Effects.Select(e => e.Row));
            await reopened.SaveAsync();
            Assert.Equal(["Buff_Caffeinated"], PlayerSaveReader.ReadFromFile(path).ActiveBuffs.Select(b => b.BuffRow));
        }
        finally { directory.Delete(recursive: true); }
    }

    [SkippableFact]
    public async Task File_session_can_swap_the_only_effect_for_a_new_one()
    {
        Skip.If(Fixtures.CascadeDir is null, "the Steam world fixture is not in this checkout");
        var (session, path, directory) = OpenCopy("Player_76561198179787042.sav");
        try
        {
            var effects = session;
            var original = Assert.Single(effects.Effects);
            await effects.RemoveEffectAsync(original);
            await effects.AddEffectAsync(Caffeinated);
            await session.SaveAsync();
            Assert.Equal(["Buff_Caffeinated"], PlayerSaveReader.ReadFromFile(path).ActiveBuffs.Select(b => b.BuffRow));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public void Debuffs_are_the_Debuff_rows_except_being_underwater()
    {
        Assert.True(PlayerEffectEdit.IsDebuffRow("Debuff_BrokenLeg"));
        Assert.False(PlayerEffectEdit.IsDebuffRow("Debuff_Underwater"));
        Assert.False(PlayerEffectEdit.IsDebuffRow("Buff_Caffeinated"));
        Assert.False(PlayerEffectEdit.IsDebuffRow("Debuff"));
    }

    [Fact]
    public async Task Live_session_lists_adds_removes_blocks_and_toggles()
    {
        var channel = new FakeChannel();
        var session = await LivePlayerConditionsSession.ConnectAsync(new LivePlayerConditionsChannel(channel), "p1");

        Assert.True(session.AppliesImmediately);
        Assert.True(session.CanEditEffects);
        Assert.False(session.OnlySavedEffects);
        var broken = session.Effects[0];
        Assert.Equal("Debuff_BrokenLeg", broken.Row);
        Assert.Equal("EBodyLimbs::LeftLeg", broken.Limb);
        Assert.True(broken.Permanent);
        Assert.Equal(40, session.Effects[1].SecondsLeft);
        Assert.Null(session.Effects[1].Limb);
        Assert.True(session.Toggles["noHunger"]);

        await session.AddEffectAsync(Caffeinated);
        Assert.Equal(("effects.add", "Buff_Caffeinated", 300.0), (channel.Last, channel.Row, channel.Seconds));
        await session.AddEffectAsync(Stinky);
        Assert.Null(channel.Seconds);

        await session.RemoveEffectAsync(broken);
        Assert.Equal(("effects.remove", "Debuff_BrokenLeg", 7), (channel.Last, channel.Row, channel.Limb));

        await session.ClearDebuffsAsync();
        Assert.Equal("effects.clear", channel.Last);

        await session.SetBlockNewDebuffsAsync(true);
        Assert.Equal("effects.block", channel.Last);
        Assert.True(session.BlockNewDebuffs);

        await session.SetToggleAsync("masterKey", true);
        Assert.Equal("toggles.set", channel.Last);
        Assert.True(session.Toggles["masterKey"]);
        Assert.All(channel.PlayerIds, id => Assert.Equal("p1", id));
    }

    [Fact]
    public async Task Vitals_without_conditions_from_an_older_agent_are_hidden_and_never_written()
    {
        var channel = new FakeChannel { OldAgent = true };
        var vitals = await LivePlayerVitalsSession.ConnectAsync(new LivePlayerVitalsChannel(channel));
        Assert.False(vitals.Vitals.HasEnvironmentStats);
        vitals.Vitals.Hunger = 10;
        await vitals.SaveAsync();
        Assert.False(channel.SetPayload!.Value.TryGetProperty("radiation", out _));

        var current = new FakeChannel();
        var live = await LivePlayerVitalsSession.ConnectAsync(new LivePlayerVitalsChannel(current));
        Assert.True(live.Vitals.HasEnvironmentStats);
        Assert.Equal(12, live.Vitals.Radiation);
        Assert.Equal(37, live.Vitals.NormalBodyTemperature);
        live.Vitals.Radiation = 0;
        await live.SaveAsync();
        Assert.Equal(0, current.SetPayload!.Value.GetProperty("radiation").GetDouble());
        Assert.Equal(31, current.SetPayload!.Value.GetProperty("bodyTemperature").GetDouble());
    }

    private sealed class FakeChannel : ILiveGameChannel
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        private bool _blocked;
        private readonly Dictionary<string, bool> _toggles = new() { ["noHunger"] = true, ["masterKey"] = false };
        public bool OldAgent { get; init; }
        public string? Last { get; private set; }
        public string? Row { get; private set; }
        public double? Seconds { get; private set; }
        public int? Limb { get; private set; }
        public JsonElement? SetPayload { get; private set; }
        public List<string?> PlayerIds { get; } = [];
        public LiveConnectionState State => LiveConnectionState.Connected;
        public event Action<LiveConnectionState>? StateChanged { add { } remove { } }
        public Task ConnectAsync(LiveConnectionInfo info, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<T> RequestAsync<T>(string command, object? payload, CancellationToken cancellationToken = default)
        {
            var wire = JsonSerializer.SerializeToElement(payload, Options);
            if (command.StartsWith("effects", StringComparison.Ordinal) || command.StartsWith("toggles", StringComparison.Ordinal))
                PlayerIds.Add(wire.ValueKind == JsonValueKind.Object && wire.TryGetProperty("playerId", out var id) ? id.GetString() : null);
            object? reply = command switch
            {
                "effects.list" => new
                {
                    effects = new object[]
                    {
                        new { row = "Debuff_BrokenLeg", limb = 7, expireTime = -1.0 },
                        new { row = "Buff_Caffeinated", limb = 0, expireTime = 140.0 },
                    },
                    now = 100.0, blockDebuffs = _blocked, isHost = true,
                },
                "toggles.get" => new { toggles = _toggles },
                "toggles.set" => Apply(wire),
                "effects.block" => new { blockDebuffs = _blocked = wire.GetProperty("enabled").GetBoolean() },
                "effects.clear" => new { removed = 1 },
                "vitals.get" => OldAgent
                    ? new { hunger = 50.0, thirst = 50.0, sanity = 50.0, fatigue = 0.0, continence = 50.0, money = 0, head = 70.0, torso = 100.0, leftArm = 100.0, rightArm = 100.0, leftLeg = 100.0, rightLeg = 100.0 }
                    : (object)new { hunger = 50.0, thirst = 50.0, sanity = 50.0, fatigue = 0.0, continence = 50.0, money = 0, head = 70.0, torso = 100.0, leftArm = 100.0, rightArm = 100.0, leftLeg = 100.0, rightLeg = 100.0, radiation = 12.0, bodyTemperature = 31.0, normalBodyTemperature = 37.0 },
                _ => null,
            };
            if (command is "effects.add" or "effects.remove")
            {
                Row = wire.GetProperty("row").GetString();
                Seconds = wire.TryGetProperty("seconds", out var seconds) ? seconds.GetDouble() : null;
                Limb = wire.TryGetProperty("limb", out var limb) ? limb.GetInt32() : null;
            }
            if (command == "vitals.set") SetPayload = wire;
            if (command != "effects.list" && command != "toggles.get" && command != "vitals.get") Last = command;
            return Task.FromResult(reply is null ? default(T)! : JsonSerializer.SerializeToElement(reply, Options).Deserialize<T>(Options)!);
        }

        private object Apply(JsonElement wire)
        {
            foreach (var property in wire.GetProperty("toggles").EnumerateObject()) _toggles[property.Name] = property.Value.GetBoolean();
            return new { toggles = _toggles };
        }
    }
}
