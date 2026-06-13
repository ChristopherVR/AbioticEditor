using AbioticEditor.Core.PlayerSaves;
using AbioticEditor.Plugins;
using AbioticEditor.Plugins.Saves;

namespace AbioticEditor.Samples.MaxSkills;

/// <summary>
/// Entry point. The host finds this single <see cref="IAbioticPlugin"/> and calls
/// <see cref="Configure"/> once to register what the plugin provides - here, one save
/// operation.
/// </summary>
public sealed class MaxSkillsPlugin : IAbioticPlugin
{
    public void Configure(IPluginRegistry registry, IPluginHost host)
    {
        host.Log.Info("Max Skills plugin configured.");
        registry.AddSaveOperation(new MaxSkillsOperation());
    }
}

/// <summary>
/// Raises every skill on a player save to a target level (default 10, max 20). A practical
/// example of the "scripted edit" extension point: it reads the typed model over the save
/// the host already loaded, recomputes each skill's XP from the level threshold, applies the
/// change, and leaves persistence (backup + write) to the host.
/// </summary>
public sealed class MaxSkillsOperation : ISaveOperation
{
    public string Id => "max-skills";

    public string DisplayName => "Max Skills";

    public string Description =>
        "Sets every skill's XP to the threshold for a target level (default 10, capped at 20). "
        + "Existing higher skills are left untouched.";

    public SaveKind AppliesTo => SaveKind.Player;

    public IReadOnlyList<SaveOperationParameter> Parameters { get; } = new[]
    {
        new SaveOperationParameter(
            "level",
            $"Target skill level 1-{SkillCatalog.MaxLevel} (default 10).",
            Required: false,
            DefaultValue: "10"),
    };

    public Task<SaveOperationResult> ExecuteAsync(ISaveOperationContext context, CancellationToken cancellationToken = default)
    {
        // Parse and clamp the requested level.
        var raw = context.GetParameter("level", "10");
        if (!int.TryParse(raw, out var level))
        {
            return Task.FromResult(SaveOperationResult.Failed($"'level' must be a whole number, got '{raw}'."));
        }
        level = Math.Clamp(level, 1, SkillCatalog.MaxLevel);
        var targetXp = SkillCatalog.XpForLevel(level);

        // Read the typed model over the host's already-loaded save (data.Raw IS context.Save,
        // so the writer mutates the very instance the host will persist).
        var data = PlayerSaveReader.ReadFrom(context.Save);
        if (data.Skills.Count == 0)
        {
            return Task.FromResult(SaveOperationResult.NoChange("this save has no skills array to edit."));
        }

        var changed = 0;
        var updated = new List<PlayerSkill>(data.Skills.Count);
        foreach (var skill in data.Skills)
        {
            // Only raise skills that are below the target; never lower an over-cap skill.
            if (skill.Xp < targetXp)
            {
                updated.Add(skill with { Xp = targetXp });
                changed++;
            }
            else
            {
                updated.Add(skill);
            }
        }

        if (changed == 0)
        {
            return Task.FromResult(SaveOperationResult.NoChange(
                $"every skill is already at or above level {level}."));
        }

        PlayerSaveWriter.ApplySkills(data, updated);
        context.MarkChanged();
        context.Log.Info($"raised {changed} skill(s) to level {level}.");

        return Task.FromResult(SaveOperationResult.Ok(
            $"raised {changed} skill(s) to level {level}.", changed));
    }
}
