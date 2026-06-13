namespace AbioticEditor.App.Views.Player;

/// <summary>Skills tab: adaptive card grid with level/XP editors + milestone chips.</summary>
public partial class PlayerSkillsTab : ContentView
{
    public PlayerSkillsTab()
    {
        InitializeComponent();
    }

    private async void OnMaxAllSkills(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }
            && await ViewUtils.ConfirmBulkAsync(this, "set all 15 skills to level 20"))
        {
            pe.MaxAllSkillsCommand.Execute(null);
        }
    }

    private async void OnMilestoneTapped(object? sender, TappedEventArgs e)
    {
        if (ViewUtils.Vm(this) is not { PlayerEditor: { } pe }) return;
        var milestone = ViewUtils.FindBoundContext<ViewModels.SkillMilestoneViewModel>(sender);
        if (milestone is null) return;
        // A sealed (locked + hidden) perk first asks to override clearance; on reveal it
        // opens straight into the detail card. An unlocked/revealed one toggles the card.
        if (milestone.IsConcealed)
        {
            if (await milestone.RevealAsync()) pe.SelectedMilestone = milestone;
            return;
        }
        pe.SelectedMilestone = ReferenceEquals(pe.SelectedMilestone, milestone) ? null : milestone;
    }
}
