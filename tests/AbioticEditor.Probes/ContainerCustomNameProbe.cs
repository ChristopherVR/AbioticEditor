using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;
using AbioticEditor.Core.WorldSaves.Features;

namespace AbioticEditor.Tests;

public sealed class ContainerCustomNameProbe
{
    [Fact]
    public void Dump_deployed_object_custom_text_display_by_class()
    {
        var output = Environment.GetEnvironmentVariable("CONTAINER_NAME_PROBE_OUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var path = Path.Combine(Fixtures.CascadeDir ?? "", "WorldSave_Facility.sav");
        if (!File.Exists(path)) return;
        var save = WorldSaveReader.ReadFromFile(path).Raw;
        var lines = new List<string>();
        foreach (var entry in WorldMapAccessor.Entries(save, "DeployedObjectMap"))
        {
            var tag = entry.Props.FindByPrefix("CustomTextDisplay_");
            var text = tag?.Property?.Value?.ToString();
            var hasInv = entry.Props.FindByPrefix("ContainerInventories_") is not null;
            var classTag = entry.Props.FindByPrefix("Class_")?.Property?.Value?.ToString() ?? "?";
            lines.Add($"tagPresent={tag is not null} hasInv={hasInv} text=[{text}] class={classTag}");
        }
        File.WriteAllLines(output, lines);
    }
}
