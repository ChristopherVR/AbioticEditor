using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>Ini editor: section/key/value editing for Admin.ini, SandboxSettings.ini etc.</summary>
public partial class IniEditorView : ContentView
{
    public IniEditorView()
    {
        InitializeComponent();
    }

    // The view's own BindingContext is the IniEditor VM only inside the inner stack;
    // the ContentView itself still inherits MainViewModel.
    private void OnIniSaveClicked(object? sender, EventArgs e) => ViewUtils.Vm(this)?.IniEditor?.Save();

    private void OnIniRevertClicked(object? sender, EventArgs e) => ViewUtils.Vm(this)?.IniEditor?.Revert();

    private void OnIniAddEntryClicked(object? sender, EventArgs e)
        => ViewUtils.FindBoundContext<IniSectionViewModel>(sender)?.AddEntry();

    private void OnIniRemoveEntryClicked(object? sender, EventArgs e)
    {
        var entry = ViewUtils.FindBoundContext<IniEntryViewModel>(sender);
        if (entry is null) return;
        // The remove button binds to the entry; the owning section sits further up the
        // visual tree as the section template's BindingContext.
        if (sender is Element el)
        {
            for (var p = el.Parent; p is not null; p = p.Parent)
            {
                if (p is BindableObject bo && bo.BindingContext is IniSectionViewModel section)
                {
                    section.RemoveEntry(entry);
                    return;
                }
            }
        }
    }
}
