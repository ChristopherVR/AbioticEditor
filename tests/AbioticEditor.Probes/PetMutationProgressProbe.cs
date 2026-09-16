using AbioticEditor.Core.PlayerSaves;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>Scratch probe (not part of normal test runs): dumps carried-pet MutationProgress
/// values across every player fixture, to ground a live/offline edit-clamp value since DT_Pets
/// carries no explicit mutation-progress threshold field.</summary>
public class PetMutationProgressProbe
{
    private readonly ITestOutputHelper _output;

    public PetMutationProgressProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dump_MutationProgress_AcrossFixturePlayers()
    {
        var roots = new[] { Fixtures.ServerWorldsDir, Fixtures.ClientSavedDir, Fixtures.CascadeDir }
            .Where(d => d is not null).Select(d => d!).ToList();
        if (roots.Count == 0) { _output.WriteLine("no fixtures"); return; }

        var max = 0;
        foreach (var root in roots)
        {
            foreach (var f in Directory.EnumerateFiles(root, "Player_*.sav", SearchOption.AllDirectories))
            {
                try
                {
                    var pets = PlayerSaveReader.ReadFromFile(f).CarriedPets;
                    foreach (var p in pets)
                    {
                        _output.WriteLine($"{Path.GetFileName(f)} slot={p.Slot}/{p.Index} item={p.ItemRow} xp={p.Xp} mutationProgress={p.MutationProgress} petMutation={p.PetMutation}");
                        if (p.MutationProgress > max) max = p.MutationProgress;
                    }
                }
                catch (Exception ex) { _output.WriteLine($"{f}: {ex.Message}"); }
            }
        }
        _output.WriteLine($"max mutationProgress seen = {max}");
    }
}
