using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AbioticEditor.App.Services;
using AbioticEditor.Core.PlayerSaves;

namespace AbioticEditor.App.ViewModels;

/// <summary>
/// Mutable view-model for one positional skill entry. Level and XP are two views of the
/// same value: setting Level snaps XP to that level's threshold; setting XP re-derives
/// Level.
/// </summary>
public sealed class SkillViewModel : INotifyPropertyChanged
{
    private float _xp;
    private float _multiplier;

    public SkillViewModel(PlayerSkill skill, SkillDefinition definition, string? iconPath)
    {
        OriginalSkill = skill;
        Definition = definition;
        _iconPath = iconPath;
        _xp = skill.Xp;
        _multiplier = skill.XpMultiplier;

        MaxCommand = new RelayCommand(() => Level = SkillCatalog.MaxLevel);
        Milestones = SkillMilestoneCatalog.For(definition.DisplayName)
            .Select(m => new SkillMilestoneViewModel(this, m))
            .ToList();
    }

    /// <summary>Milestone perk track (wiki-sourced; levels are irregular per skill).</summary>
    public IReadOnlyList<SkillMilestoneViewModel> Milestones { get; }

    public bool HasMilestones => Milestones.Count > 0;

    /// <summary>Per-level passive bonus, e.g. "+2 carrying capacity".</summary>
    public string? PassiveText => SkillMilestoneCatalog.PassiveFor(Definition.DisplayName);

    public PlayerSkill OriginalSkill { get; private set; }
    public SkillDefinition Definition { get; }

    private string? _iconPath;

    /// <summary>Set asynchronously after construction - icon extraction happens off the UI thread.</summary>
    public string? IconPath
    {
        get => _iconPath;
        internal set
        {
            if (_iconPath == value) return;
            _iconPath = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIcon)));
        }
    }

    public bool HasIcon => IconPath is not null;

    public int Index => Definition.SaveIndex;
    public string DisplayName => Definition.DisplayName;
    public string? Description => Definition.Description;

    public ICommand MaxCommand { get; }

    public float Xp
    {
        get => _xp;
        set
        {
            var clamped = Math.Max(0, value);
            if (Math.Abs(_xp - clamped) < float.Epsilon) return;
            var old = _xp;
            _xp = clamped;
            if (Core.Diagnostics.EditorLog.Enabled)
            {
                Core.Diagnostics.EditorLog.Info("Edit", $"Skill {DisplayName}: XP {old:F0} → {clamped:F0} (level {Level})");
            }
            OnChanged();
        }
    }

    public int Level
    {
        get => SkillCatalog.LevelForXp(_xp);
        set
        {
            var clamped = Math.Clamp(value, 0, SkillCatalog.MaxLevel);
            if (clamped == Level) return;
            Xp = SkillCatalog.XpForLevel(clamped);
        }
    }

    public float Multiplier
    {
        get => _multiplier;
        set
        {
            if (Math.Abs(_multiplier - value) < float.Epsilon) return;
            _multiplier = value;
            OnChanged();
        }
    }

    /// <summary>0..1 progress toward the next level (1.0 at max level).</summary>
    public double LevelProgress
    {
        get
        {
            var level = Level;
            if (level >= SkillCatalog.MaxLevel) return 1;
            var floor = SkillCatalog.XpForLevel(level);
            var ceil = SkillCatalog.XpForLevel(level + 1);
            return ceil <= floor ? 0 : Math.Clamp((_xp - floor) / (ceil - floor), 0, 1);
        }
    }

    public string XpText => $"{_xp:F0} XP";

    /// <summary>
    /// The XP slider's ceiling: the level-20 threshold - or the save's own XP when the
    /// player kept earning past it (end-game saves exceed the cap, and the slider's
    /// Maximum must never clamp-write real values away). Deliberately not re-notified
    /// on XP changes so the ceiling stays put while dragging.
    /// </summary>
    public double MaxXp => Math.Max(SkillCatalog.XpForLevel(SkillCatalog.MaxLevel), _xp);

    /// <summary>
    /// Slider-friendly XP. Sub-unit drift from the slider's double/float round-trip on
    /// binding init must not register as an edit (same gotcha as the F0 entries).
    /// </summary>
    public double XpSliderValue
    {
        get => _xp;
        set
        {
            // Binding-order race: the platform slider can clamp to its DEFAULT range
            // (0..1) and write that back before the bound Maximum applies - never let
            // an init clamp wipe real XP. Sub-1 drags are meaningless anyway.
            if (value <= 1 && _xp > 1) return;
            if (Math.Abs(_xp - value) < 0.5) return;
            Xp = (float)value;
        }
    }

    public bool IsMaxed => Level >= SkillCatalog.MaxLevel;
    public bool IsDirty =>
        Math.Abs(_xp - OriginalSkill.Xp) > 0.001f ||
        Math.Abs(_multiplier - OriginalSkill.XpMultiplier) > 0.001f;

    public PlayerSkill ToCurrentSkill() => OriginalSkill with { Xp = _xp, XpMultiplier = _multiplier };

    public void AcceptCurrentAsBaseline()
    {
        OriginalSkill = ToCurrentSkill();
        OnChanged();
    }

    public void Revert()
    {
        _xp = OriginalSkill.Xp;
        _multiplier = OriginalSkill.XpMultiplier;
        OnChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnChanged([CallerMemberName] string? _ = null)
    {
        foreach (var p in new[]
        {
            nameof(Xp), nameof(XpSliderValue), nameof(Level), nameof(Multiplier), nameof(LevelProgress),
            nameof(XpText), nameof(IsMaxed), nameof(IsDirty),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
        foreach (var m in Milestones)
        {
            m.RefreshUnlockState();
        }
    }
}

/// <summary>One milestone chip in a skill's perk track, with a tap-to-open detail card.</summary>
public sealed class SkillMilestoneViewModel : INotifyPropertyChanged
{
    private readonly SkillViewModel _owner;

    public SkillMilestoneViewModel(SkillViewModel owner, SkillMilestone milestone)
    {
        _owner = owner;
        Milestone = milestone;
    }

    public SkillMilestone Milestone { get; }
    public int Level => Milestone.Level;
    public bool IsUnlocked => _owner.Level >= Milestone.Level;

    // ---------- the skill this milestone belongs to (for the detail card) ----------

    public string SkillName => _owner.DisplayName;
    public string? SkillIconPath => _owner.IconPath;
    public bool HasSkillIcon => _owner.HasIcon;
    public int CurrentSkillLevel => _owner.Level;

    // ---------- hidden-until-unlocked concealment ----------
    // In-game a milestone perk stays hidden ("???") until the player reaches its level.
    // We mirror that: a LOCKED milestone is sealed behind spoiler protection until the
    // skill reaches the level (then it auto-reveals) or the user overrides clearance.

    /// <summary>Per-item reveal key.</summary>
    public string SpoilerKey => SpoilerService.Key(SpoilerService.Skill, $"{_owner.DisplayName}:{Milestone.Level}");

    /// <summary>A locked milestone is a future perk - concealed while protection is on.</summary>
    public bool IsConcealed => SpoilerService.ShouldConceal(SpoilerKey, !IsUnlocked);

    public string ShownPerk => SpoilerService.Mask(Milestone.Perk, IsConcealed, SpoilerService.ClassifiedShort);
    public string ShownEffect => IsConcealed
        ? "Hidden until unlocked - reach this level (or tap to override clearance) to see the perk."
        : Milestone.Effect;

    /// <summary>Chip label: the level requirement always shows; the perk masks while sealed.</summary>
    public string ChipText => $"{Milestone.Level} · {ShownPerk}";

    public string Tooltip => IsConcealed
        ? $"Level {Milestone.Level} - hidden perk (tap to reveal)"
        : $"Level {Milestone.Level} - {Milestone.Perk}: {Milestone.Effect}";

    // ---------- detail-card text ----------

    public string LevelText => $"LEVEL {Milestone.Level}";
    public string StatusText => IsUnlocked ? "UNLOCKED" : IsConcealed ? "SEALED" : "LOCKED";

    /// <summary>How far this skill is from the milestone (or confirmation it's unlocked).</summary>
    public string RequirementText
    {
        get
        {
            if (IsUnlocked) return $"Unlocked - this skill is level {_owner.Level} (needs {Milestone.Level}).";
            var levelsToGo = Milestone.Level - _owner.Level;
            var xpToGo = SkillCatalog.XpForLevel(Milestone.Level) - _owner.Xp;
            var levels = levelsToGo == 1 ? "1 level" : $"{levelsToGo} levels";
            return $"Locked - reach level {Milestone.Level} ({levels} / {Math.Max(0, xpToGo):F0} XP to go).";
        }
    }

    /// <summary>Re-notifies unlock + every derived/masked member (owner level changed).</summary>
    public void RefreshUnlockState() => NotifyAll();

    private void NotifyAll()
    {
        foreach (var n in new[]
        {
            nameof(IsUnlocked), nameof(IsConcealed), nameof(ShownPerk), nameof(ShownEffect),
            nameof(ChipText), nameof(Tooltip), nameof(StatusText), nameof(RequirementText),
            nameof(CurrentSkillLevel),
        })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>Prompts to override clearance on a sealed perk; reveals it permanently on confirm.</summary>
    public async Task<bool> RevealAsync()
    {
        if (!IsConcealed) return true;
        if (await SpoilerPrompt.RevealAsync($"This {_owner.DisplayName} perk", SpoilerKey))
        {
            NotifyAll();
            return true;
        }
        return false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
