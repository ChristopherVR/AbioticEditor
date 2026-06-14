namespace AbioticEditor.App.ViewModels;

/// <summary>A sibling save file offered as a cross-save pet-move destination.</summary>
public sealed record SaveTarget(string Path, string Name)
{
    public override string ToString() => Name;
}
