using AbioticEditor.Core.WorldSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>
/// Fixture tests for the read-only <see cref="NarrativeNpcInspector"/>: it must agree with the
/// existing <see cref="WorldNpc"/> reader on the fields they share, never mutate the save, and
/// report what the saved entries really carry beyond the dead flag and state.
/// </summary>
public sealed class NarrativeNpcInspectorTests(ITestOutputHelper output)
{
    [Fact]
    public void Inspector_agrees_with_the_WorldNpc_reader_on_shared_fields()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        var checkedEntries = 0;
        foreach (var (path, data) in AllFixtureSaves.WorldSaves)
        {
            var details = NarrativeNpcInspector.ReadNarrative(data).ToDictionary(d => d.Id, StringComparer.Ordinal);
            var npcs = data.Npcs.Where(n => !n.IsPet).ToList();
            Assert.Equal(npcs.Count, details.Count);
            foreach (var npc in npcs)
            {
                var d = details[npc.Id];
                Assert.Equal(npc.IsDead, d.IsDead);
                Assert.Equal(npc.State, d.NarrativeState);
                Assert.Equal(npc.X, d.X);
                checkedEntries++;
            }
        }
        Assert.True(checkedEntries > 0);
    }

    [Fact]
    public void Reading_does_not_change_the_save_bytes()
    {
        var facility = AllFixtureSaves.WorldSaves.FirstOrDefault(w => w.Path.EndsWith("WorldSave_Facility_Pens.sav", StringComparison.Ordinal));
        if (facility.Data is null) return;

        byte[] Bytes()
        {
            using var ms = new MemoryStream();
            facility.Data.Raw.WriteTo(ms);
            return ms.ToArray();
        }
        var before = Bytes();
        _ = NarrativeNpcInspector.ReadNarrative(facility.Data);
        _ = NarrativeNpcInspector.ReadPets(facility.Data);
        Assert.Equal(before, Bytes());
    }

    [Fact]
    public void Extra_data_census_across_fixtures()
    {
        if (AllFixtureSaves.WorldSaves.Count == 0) return;

        var all = AllFixtureSaves.WorldSaves.SelectMany(w => NarrativeNpcInspector.ReadNarrative(w.Data)).ToList();
        var pets = AllFixtureSaves.WorldSaves.SelectMany(w => NarrativeNpcInspector.ReadPets(w.Data)).ToList();
        output.WriteLine($"narrative entries={all.Count} dead={all.Count(d => d.IsDead)} named={all.Count(d => d.CustomName is not null)}"
            + $" withClass={all.Count(d => d.NpcClass is not null)} withLocation={all.Count(d => d.HasLocation)}"
            + $" withHealth={all.Count(d => d.LimbHealth.Count > 0)} withDynamic={all.Count(d => d.DynamicProperties.Count > 0)}"
            + $" unmodeledMembers={all.Count(d => d.UnmodeledMembers.Count > 0)}");
        output.WriteLine($"pet entries={pets.Count} named={pets.Count(d => d.CustomName is not null)}"
            + $" withClass={pets.Count(d => d.NpcClass is not null)} withLocation={pets.Count(d => d.HasLocation)}"
            + $" withHealth={pets.Count(d => d.LimbHealth.Count > 0)} withDynamic={pets.Count(d => d.DynamicProperties.Count > 0)}"
            + $" unmodeledMembers={pets.Count(d => d.UnmodeledMembers.Count > 0)}");

        // Fixture-proven (534 narrative entries, 3 worlds): the game fills nothing but IsDead and
        // NarrativeState for narrative NPCs. Names, classes, locations, health and dynamic
        // properties are reserve members. If a future save breaks this, the doc needs a refresh.
        Assert.All(all, d => Assert.False(d.HasExtraData, d.Id));

        // Non-default states only ever appear on dead entries (NewEnumerator0 and 2), so the stage
        // is a script marker for removed actors, not a free-standing "alive" selector.
        Assert.All(all.Where(d => d.NarrativeState is not null && !d.NarrativeState.EndsWith("NewEnumerator3", StringComparison.Ordinal)),
            d => Assert.True(d.IsDead, d.Id));

        // Pet entries share the struct and fill every reserve member.
        Assert.All(pets, d => Assert.NotNull(d.NpcClass));
        Assert.All(pets, d => Assert.NotEmpty(d.LimbHealth));
        Assert.All(pets, d => Assert.NotEmpty(d.DynamicProperties));

        foreach (var g in all.GroupBy(d => d.NarrativeState).OrderBy(g => g.Key))
        {
            output.WriteLine($"narrative state {g.Key}: {g.Count()} (dead {g.Count(d => d.IsDead)})");
        }
        foreach (var d in all.Where(d => d.HasExtraData).Take(15))
        {
            output.WriteLine($"extra: {d.Id} name={d.CustomName} class={d.NpcClass} loc=({d.X},{d.Y},{d.Z}) hp={d.LimbHealth.Count}"
                + $" dyn=[{string.Join(",", d.DynamicProperties.Select(p => p.Key + "=" + p.Value))}]");
        }
        foreach (var d in pets.Take(6))
        {
            output.WriteLine($"pet: {d.Id} name={d.CustomName} class={d.NpcClass} hp={string.Join(",", d.LimbHealth.Select(kv => kv.Key + "=" + kv.Value))}"
                + $" dyn=[{string.Join(",", d.DynamicProperties.Select(p => p.Key + "=" + p.Value))}]");
        }
    }
}
