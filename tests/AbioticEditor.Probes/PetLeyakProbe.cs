using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using UeSaveGame;
using UeSaveGame.PropertyTypes;
using UeSaveGame.StructData;
using Xunit.Abstractions;

namespace AbioticEditor.Tests;

/// <summary>Dumps PetNPC / LeyakContainmentIDs presence and struct shape from the fixtures.</summary>
public class PetLeyakProbe
{
    private readonly ITestOutputHelper _output;

    public PetLeyakProbe(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Dump_PetAndLeyak_AcrossFixtureSaves()
    {
        if (Fixtures.ServerWorldsDir is null) { _output.WriteLine("no fixture"); return; }

        foreach (var sav in Directory.EnumerateFiles(Fixtures.ServerWorldsDir, "WorldSave_*.sav"))
        {
            SaveGame save;
            try
            {
                using var fs = File.OpenRead(sav);
                save = SaveGame.LoadFrom(fs);
            }
            catch { continue; }

            var pets = save.Properties.FindByPrefix("PetNPC");
            var leyak = save.Properties.FindByPrefix("LeyakContainmentIDs");
            if (pets is null && leyak is null) continue;

            _output.WriteLine($"=== {Path.GetFileName(sav)} ===");
            if (leyak?.Property is MapProperty lm && lm.Value is not null)
            {
                _output.WriteLine($"  LeyakContainmentIDs: {lm.Value.Count} entries");
                foreach (var kv in lm.Value.Take(5))
                {
                    _output.WriteLine($"    {kv.Key} -> {kv.Value}");
                }
            }
            if (pets?.Property is MapProperty pm && pm.Value is not null)
            {
                _output.WriteLine($"  PetNPC: {pm.Value.Count} entries");
                foreach (var kv in pm.Value.Take(3))
                {
                    _output.WriteLine($"    key={kv.Key}");
                    if (kv.Value is StructProperty sp && sp.Value is PropertiesStruct ps)
                    {
                        foreach (var p in ps.Properties)
                        {
                            var s = p.Property?.Value?.ToString() ?? "(null)";
                            if (s.Length > 140) s = s[..140];
                            _output.WriteLine($"      {p.Name?.Value} ({p.Property?.GetType().Name}) = {s}");
                        }
                    }
                }
            }
        }
        _output.WriteLine("scan done");
    }
}
