using AbioticEditor.Core.Compare;
using AbioticEditor.Core.Compatibility;
using AbioticEditor.Core.Diagnostics;
using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Core.Saves;
using AbioticEditor.Core.WorldSaves;

namespace AbioticEditor.Web.Services;

/// <summary>Read-only comparisons of two local save files or folders.</summary>
public sealed class SaveComparisonService(SaveSemanticDiff semanticDiff)
{
    public ComparisonResult Compare(string? leftInput, string? rightInput)
    {
        var left = NormalizePath(leftInput, "first");
        var right = NormalizePath(rightInput, "second");
        var leftDirectory = Directory.Exists(left);
        var rightDirectory = Directory.Exists(right);

        if (leftDirectory != rightDirectory)
            throw new ArgumentException("Choose either two .sav files or two save folders.");

        if (leftDirectory)
        {
            var folder = SaveFolderComparer.Compare(left, right);
            return ComparisonResult.ForFolder(folder, BuildFolderSemantics(folder));
        }

        if (!File.Exists(left) || !File.Exists(right))
            throw new FileNotFoundException("Both save files must exist.");
        if (!string.Equals(Path.GetExtension(left), ".sav", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(right), ".sav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose Abiotic Factor .sav files.");

        var diff = SaveComparer.CompareFiles(left, right);
        var semantic = TryBuildSemantic(left, right);
        return ComparisonResult.ForSave(diff, semantic);
    }

    /// <summary>
    /// Builds a domain-aware semantic diff when both files are the same kind: two player saves
    /// -> player sections, two world/metadata saves -> world sections. Null when they aren't a
    /// matched, supported pair (the raw property diff covers those).
    /// </summary>
    private (string Kind, List<SemanticSection> Sections)? TryBuildSemantic(string a, string b)
    {
        try
        {
            var kindA = ClassifyKind(a);
            var kindB = ClassifyKind(b);
            if (kindA != kindB) return null;

            if (kindA == SaveKind.Character)
            {
                var pa = PlayerSaveReader.ReadFromFile(a);
                var pb = PlayerSaveReader.ReadFromFile(b);
                return ("PLAYER", semanticDiff.BuildPlayer(pa, pb));
            }

            if (kindA is SaveKind.World or SaveKind.Metadata)
            {
                var wa = WorldSaveReader.ReadFromFile(a);
                var wb = WorldSaveReader.ReadFromFile(b);
                return ("WORLD", semanticDiff.BuildWorld(wa, wb));
            }

            return null;
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Compare", $"Semantic diff unavailable for '{a}' / '{b}'", ex);
            return null;
        }
    }

    /// <summary>
    /// Builds the same readable, domain-aware summary the file-vs-file view gets for every
    /// differing player/world pair in a folder comparison, keyed by relative path. Without
    /// this, folder mode only ever showed the raw property diff, so a real gameplay change
    /// (money, skills, items) could sit in a list of hundreds of "noise" properties and read
    /// as if nothing meaningful changed.
    /// </summary>
    private Dictionary<string, (string Kind, List<SemanticSection> Sections)> BuildFolderSemantics(FolderDiff folder)
    {
        var result = new Dictionary<string, (string Kind, List<SemanticSection> Sections)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in folder.Files)
        {
            if (file.Status != FolderEntryStatus.Differs) continue;
            if (file.Diff?.LeftLabel is not { } left || file.Diff.RightLabel is not { } right) continue;

            var semantic = TryBuildSemantic(left, right);
            if (semantic is { } value) result[file.RelativePath] = value;
        }
        return result;
    }

    /// <summary>Classifies a save's kind from its header alone (cheap, never parses the body).</summary>
    private static SaveKind ClassifyKind(string path)
    {
        try
        {
            var (saveClass, _) = SaveFolderScanner.ReadHeaderInfo(path);
            return SaveVersionRegistry.KindOfClassPath(saveClass);
        }
        catch (Exception ex)
        {
            EditorLog.Warn("Compare", $"Could not read header for '{path}'", ex);
            return SaveKind.Unknown;
        }
    }

    private static string NormalizePath(string? input, string label)
    {
        if (string.IsNullOrWhiteSpace(input)) throw new ArgumentException($"Enter the {label} save path.");
        try { return Path.GetFullPath(input.Trim()); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ArgumentException($"The {label} path is not valid.", exception); }
    }
}

public sealed record ComparisonResult(
    SaveDiff? Save,
    FolderDiff? Folder,
    (string Kind, List<SemanticSection> Sections)? Semantic = null,
    IReadOnlyDictionary<string, (string Kind, List<SemanticSection> Sections)>? FolderSemantics = null)
{
    public bool IsFolder => Folder is not null;
    public static ComparisonResult ForSave(SaveDiff save, (string Kind, List<SemanticSection> Sections)? semantic = null) => new(save, null, semantic);
    public static ComparisonResult ForFolder(FolderDiff folder, IReadOnlyDictionary<string, (string Kind, List<SemanticSection> Sections)>? folderSemantics = null) => new(null, folder, null, folderSemantics);
}
