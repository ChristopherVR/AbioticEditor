using System.ComponentModel;
using AbioticEditor.App.ViewModels;

namespace AbioticEditor.App.Views;

/// <summary>
/// Always-present modal overlay bound to <see cref="DialogViewModel.Current"/>. Fades the
/// scrim and pops the card in/out so the in-app dialog feels responsive instead of a jarring
/// platform popup. Visibility is driven by the view-model's <c>IsOpen</c>; the exit animation
/// runs before the host hides so closing is smooth too.
/// </summary>
public partial class DialogHostView : ContentView
{
    private readonly DialogViewModel _vm = DialogViewModel.Current;
    private const double ScrimOpacity = 0.55;

    public DialogHostView()
    {
        InitializeComponent();
        BindingContext = _vm;
        _vm.PropertyChanged += OnDialogPropertyChanged;
    }

    private void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogViewModel.IsOpen)) return;
        if (_vm.IsOpen) _ = AnimateInAsync();
        else _ = AnimateOutAsync();
    }

    private async Task AnimateInAsync()
    {
        // Cancel any in-flight exit and present from the collapsed state.
        this.AbortAnimation("dlg");
        IsVisible = true;
        Scrim.Opacity = 0;
        Card.Opacity = 0;
        Card.Scale = 0.92;

        var scrim = Scrim.FadeToAsync(ScrimOpacity, 160, Easing.CubicOut);
        var fade = Card.FadeToAsync(1, 180, Easing.CubicOut);
        var pop = Card.ScaleToAsync(1, 200, Easing.SpringOut);
        await Task.WhenAll(scrim, fade, pop);
    }

    private async Task AnimateOutAsync()
    {
        if (!IsVisible) return;
        var scrim = Scrim.FadeToAsync(0, 140, Easing.CubicIn);
        var fade = Card.FadeToAsync(0, 130, Easing.CubicIn);
        var shrink = Card.ScaleToAsync(0.94, 140, Easing.CubicIn);
        await Task.WhenAll(scrim, fade, shrink);

        // A new dialog may have opened during the exit tween - don't hide it.
        if (!_vm.IsOpen) IsVisible = false;
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => _vm.DismissViaScrim();
}
