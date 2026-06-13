namespace AbioticEditor.App.Views.Player;

/// <summary>Recipe book tab: search/filter/category chips + wiki-style detail pane.</summary>
public partial class PlayerRecipesTab : ContentView
{
    public PlayerRecipesTab()
    {
        InitializeComponent();
    }

    private async void OnUnlockAllRecipes(object? sender, EventArgs e)
    {
        if (ViewUtils.Vm(this) is { PlayerEditor: { } pe }
            && await ViewUtils.ConfirmBulkAsync(this, "unlock every recipe for this player"))
        {
            pe.RecipeBrowser.UnlockAllCommand.Execute(null);
        }
    }
}
