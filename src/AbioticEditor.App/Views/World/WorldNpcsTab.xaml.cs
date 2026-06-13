using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views.World;

/// <summary>NPCs tab: live narrative-NPC records (story NPCs + pets).</summary>
public partial class WorldNpcsTab : ContentView
{
    public WorldNpcsTab()
    {
        InitializeComponent();
    }

    private void OnReviveNpc(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is not { } vm) return;
        var npc = ViewUtils.FindBoundContext<WorldNpcViewModel>(sender);
        npc?.Revive();
        if (npc is not null)
        {
            vm.StatusMessage = $"{npc.ActorName} revived - press SAVE to write. Story-scripted departures may reappear in-game.";
        }
    }
}
