using System.CommandLine;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.SaveClasses;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

using AbioticEditor.Core.Compatibility;

namespace AbioticEditor.Cli;

/// <summary>
/// <c>info &lt;save.sav&gt;</c> - key facts about one save. Players: identity, money,
/// skills; worlds: flag/deployable counts (plus chapter/playtime on the metadata save).
/// </summary>
internal static class InfoCommand
{
    public static Command Build(Option<bool> quiet)
    {
        var fileArg = new Argument<string>("save")
        {
            Description = "Path to a Player_*.sav or WorldSave_*.sav file.",
        };
        var jsonOpt = new Option<bool>("--json")
        {
            Description = "Emit JSON instead of the human-readable summary.",
        };

        var cmd = new Command("info", "Show key facts about a single save file.");
        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(jsonOpt);
        cmd.SetAction(parseResult => Cli.Run(() => Execute(
            parseResult.GetValue(fileArg),
            parseResult.GetValue(jsonOpt))));
        return cmd;
    }

    private static int Execute(string? file, bool json)
    {
        var path = Cli.RequireFile(file, "save file");
        var saveClass = SaveFolderScanner.ReadSaveClassFromHeader(path);

        if (saveClass?.Contains("CharacterSave", StringComparison.Ordinal) == true)
        {
            return PlayerInfo(path, json);
        }
        if (saveClass?.Contains("WorldSave", StringComparison.Ordinal) == true
            || saveClass?.Contains("WorldMetadataSave", StringComparison.Ordinal) == true)
        {
            return WorldInfo(path, saveClass, json);
        }

        throw new CliUserErrorException(
            $"not an Abiotic Factor save this tool understands (save class: '{saveClass ?? "(none)"}').");
    }

    private static int PlayerInfo(string path, bool json)
    {
        var data = PlayerSaveReader.ReadFromFile(path);
        WarnIfNewer(SaveCompatibility.WarningFor(data.Raw));

        var steamId = PlayerSaveIdentity.GetSaveIdentifier(data.Raw)
            ?? SteamIdFromFileName(path);
        var skillsWithXp = data.Skills.Count(s => s.Xp > 0);

        if (json)
        {
            Cli.WriteJson(new
            {
                path,
                kind = "player",
                saveClass = data.SaveClassName,
                abfVersion = data.AbfVersion,
                steamId,
                phd = data.Phd,
                money = data.Stats.Money,
                skillCount = data.Skills.Count,
                skillsWithXp,
                traitCount = data.Traits.Count,
                recipeCount = data.Recipes.Count,
            });
            return Cli.Ok;
        }

        Console.WriteLine($"File:     {path}");
        Console.WriteLine($"Kind:     PLAYER ({data.SaveClassName})");
        Console.WriteLine($"Version:  {Fmt(data.AbfVersion)} (editor built against v{SaveCompatibility.KnownGoodCharacterVersion})");
        Console.WriteLine($"SteamID:  {steamId ?? "(unknown)"}");
        Console.WriteLine($"PhD:      {data.Phd ?? "(none)"}");
        Console.WriteLine($"Money:    {data.Stats.Money}");
        Console.WriteLine($"Skills:   {data.Skills.Count} ({skillsWithXp} with XP)");
        Console.WriteLine($"Traits:   {data.Traits.Count}");
        Console.WriteLine($"Recipes:  {data.Recipes.Count}");
        return Cli.Ok;
    }

    private static int WorldInfo(string path, string saveClass, bool json)
    {
        var data = WorldSaveReader.ReadFromFile(path);
        WarnIfNewer(SaveCompatibility.WarningFor(data.Raw));

        var isMetadata = saveClass.Contains("WorldMetadataSave", StringComparison.Ordinal);

        if (json)
        {
            Cli.WriteJson(new
            {
                path,
                kind = isMetadata ? "world-metadata" : "world",
                saveClass = data.SaveClassName,
                abfVersion = data.AbfVersion,
                flagCount = data.Flags.Count,
                deployableCount = data.Deployables.Count,
                containerCount = data.Containers.Count,
                doorCount = data.Doors.Count,
                droppedItemCount = data.DroppedItems.Count,
                npcCount = data.Npcs.Count,
                storyChapter = data.StoryProgressionRow,
                minutesPassed = data.MinutesPassed,
            });
            return Cli.Ok;
        }

        Console.WriteLine($"File:          {path}");
        Console.WriteLine($"Kind:          {(isMetadata ? "META" : "WORLD")} ({data.SaveClassName})");
        Console.WriteLine($"Version:       {Fmt(data.AbfVersion)} (editor built against v{SaveCompatibility.KnownGoodWorldVersion})");
        Console.WriteLine($"Flags:         {data.Flags.Count}");
        Console.WriteLine($"Deployables:   {data.Deployables.Count}");
        Console.WriteLine($"Containers:    {data.Containers.Count}");
        Console.WriteLine($"Doors:         {data.Doors.Count}");
        Console.WriteLine($"Dropped items: {data.DroppedItems.Count}");
        Console.WriteLine($"NPCs:          {data.Npcs.Count}");
        if (isMetadata)
        {
            Console.WriteLine($"Story chapter: {data.StoryProgressionRow ?? "(none)"}");
            if (data.MinutesPassed is int minutes)
            {
                Console.WriteLine($"Played:        {minutes} min ({minutes / 60}h {minutes % 60}m)");
            }
        }
        return Cli.Ok;
    }

    private static void WarnIfNewer(string? warning)
    {
        if (warning is not null)
        {
            Cli.Warn(warning);
        }
    }

    private static string Fmt(int? version) => version is int v ? $"{v}" : "(unknown)";

    /// <summary>Fallback identity: the <c>Player_&lt;steamid64&gt;.sav</c> file name.</summary>
    private static string? SteamIdFromFileName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        const string prefix = "Player_";
        return stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && stem.Length > prefix.Length
            ? stem[prefix.Length..]
            : null;
    }
}
