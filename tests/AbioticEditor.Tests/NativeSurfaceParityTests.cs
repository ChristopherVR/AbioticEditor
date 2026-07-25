namespace AbioticEditor.Tests;

/// <summary>
/// Contract matrix for the workbench surfaces retained when the former desktop UI was retired.
/// Each row names the interaction and visual structure that must remain present in Razor.
/// </summary>
public sealed class NativeSurfaceParityTests
{
    public static TheoryData<string, string[]> SurfaceMatrix => new()
    {
        // "WorldTab_Raw" was replaced by the native tab strip port: the RAW tab is now RAW JSON
        // (WorldEditor_TabRawJson) and the strip is built per save kind and available feature
        // maps (BuildWorldTabs), including the metadata-only SERVER ENTITLEMENTS feature tab.
        { "Components/Pages/SaveEditorSurface.razor", ["world-editor-summary", "WorldEditor_WorldDay", "WorldEditor_TimeOfDay", "WorldEditor_DayDiscovered", "WorldEditor_TabRawJson", "BuildWorldTabs", "WorldEditor_TabQuestFlags", "FeatureTabPrefix", "WorldEntitlementsTab"] },
        { "Components/Pages/IniEditor.razor", ["ini-editor-panel", "ini-kind-badge", "Ini_SaveIni", "IniSessions.OpenDiscovered", "IniSessions.Changed"] },
        { "Components/Pages/Compare.razor", ["Compare_ModeFileVsFile", "Compare_ModeFolderVsFolder", "compare-sources", "FilePicker.PickFileAsync", "FolderPicker.PickFolderAsync"] },
        { "Components/Pages/CreateWorld.razor", ["CreateWorld_StepProgress", "CreateWorld_SegSteam", "CreateWorld_SegGamePass", "SaveConversionDirection.ToGamePass", "FolderPicker.PickFolderAsync"] },
        { "Components/Pages/GamePass.razor", ["SaveConversionDirection.ToGamePass", "SaveConversionDirection.ToSteam", "FolderPicker.PickFolderAsync", "OpenOutputFolderAsync"] },
        { "Components/Pages/WebToolHost.razor", ["WebTools.Open", "tool-frame", "@onclick=\"Close\"", "WebTools.Close"] },
    };

    [Theory]
    [MemberData(nameof(SurfaceMatrix))]
    public void Workbench_surface_retains_native_structure_and_interactions(string relativePath, string[] contracts)
    {
        var source = File.ReadAllText(Path.Combine(WebRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var contract in contracts)
            Assert.Contains(contract, source, StringComparison.Ordinal);
    }

    [Fact]
    public void Workbench_visible_copy_is_resource_backed()
    {
        var resources = File.ReadAllText(Path.Combine(WebRoot(), "Localization", "AppResources.resx"));
        var required = new[]
        {
            "WorldEditor_SectionsAria", "WorldTab_Flags", "Ini_EditorTitle", "Ini_StatusSaved",
            "Compare_OldLabel", "Compare_SaveFileType", "Plugins_ToolUnavailable",
            "CreateWorld_PlatformCardTitle", "CreateWorld_StatusWritingContainer",
        };
        foreach (var key in required)
            Assert.Contains($"name=\"{key}\"", resources, StringComparison.Ordinal);
    }

    [Fact]
    public void World_workbench_stages_clock_and_day_controls()
    {
        Assert.NotNull(Fixtures.CascadeDir);
        var facility = Path.Combine(Fixtures.CascadeDir!, "WorldSave_Facility.sav");
        var session = new AbioticEditor.Web.Models.WorldSaveSession(
            AbioticEditor.Core.WorldSaves.WorldSaveReader.ReadFromFile(facility), facility);
        Assert.NotNull(session.WorldTimeSeconds);
        Assert.NotNull(session.WorldDay);
        var originalDay = session.WorldDay!.Value;

        session.SetWorldClock(43200, originalDay + 1);

        Assert.Equal(43200, session.WorldTimeSeconds);
        Assert.Equal(originalDay + 1, session.WorldDay);
        Assert.True(session.IsDirty);
        session.Revert();
        Assert.Equal(originalDay, session.WorldDay);
    }

    private static string WebRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AbioticEditor.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "src", "AbioticEditor.Web");
    }
}
