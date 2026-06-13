using AbioticEditor.Core.Saves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;

using UeSaveGame;
using UeSaveGame.PropertyTypes;

namespace AbioticEditor.Samples.GrantFlag;

/// <summary>Entry point: registers the one save operation this plugin provides.</summary>
public sealed class GrantFlagPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        host.Log.Info("Grant Flag plugin configured.");
        registry.AddSaveOperation(new GrantFlagOperation());
    }
}

/// <summary>
/// Adds a named entry to a world save's <c>WorldFlags</c> array if it is not already present.
/// This is the "add an unknown / missing flag" fix-up: re-arm a quest trigger a patch dropped,
/// or set a flag the editor's own UI does not yet model. It edits the <see cref="SaveGame"/>
/// property tree directly through UeSaveGame, so it works even for flags Core has no vocabulary
/// for - the whole point of a forward-compatibility fix.
/// </summary>
public sealed class GrantFlagOperation : ISaveOperation
{
    public string Id => "grant-flag";

    public string DisplayName => "Grant World Flag";

    public string Description =>
        "Adds a flag name to a world save's WorldFlags array (no-op if it already exists). "
        + "Use it to re-arm a missing quest flag or set one the editor doesn't model yet.";

    public SaveKind AppliesTo => SaveKind.World;

    public IReadOnlyList<SaveOperationParameter> Parameters { get; } = new[]
    {
        new SaveOperationParameter(
            "flag",
            "The exact WorldFlag name to add, e.g. 'Office_PowerRestored'.",
            Required: true),
    };

    public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
    {
        var flag = context.GetParameter("flag").Trim();
        if (flag.Length == 0)
        {
            return Task.FromResult(SaveOperationResult.Failed("a non-empty 'flag' name is required."));
        }

        // WorldFlags is a top-level array of FString on world saves. Editing it directly keeps
        // this independent of any typed world model - it handles flags Core doesn't know.
        if (context.Save.Properties.FindByPrefix("WorldFlags")?.Property is not ArrayProperty array)
        {
            return Task.FromResult(SaveOperationResult.Failed("this save has no WorldFlags array."));
        }

        var current = (array.Value as Array)?.Cast<object?>()
            .Select(v => v?.ToString() ?? string.Empty)
            .ToList() ?? new List<string>();

        if (current.Contains(flag, StringComparer.Ordinal))
        {
            return Task.FromResult(SaveOperationResult.NoChange($"flag '{flag}' is already set."));
        }

        current.Add(flag);
        array.Value = current.Select(f => new FString(f)).ToArray();
        context.MarkChanged();
        context.Log.Info($"added world flag '{flag}'.");

        return Task.FromResult(SaveOperationResult.Ok($"added world flag '{flag}'.", 1));
    }
}
