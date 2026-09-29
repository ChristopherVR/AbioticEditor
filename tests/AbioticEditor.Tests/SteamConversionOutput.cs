namespace AbioticEditor.Tests;

/// <summary>
/// Tests that convert a Game Pass fixture to Steam share one output folder on disk (the normal
/// Steam save location, named after the world), so they must not run in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SteamConversionOutput
{
    public const string Name = "Steam conversion output";
}
