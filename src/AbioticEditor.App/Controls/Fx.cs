namespace AbioticEditor.App.Controls;

/// <summary>
/// Attached motion helpers that make the editor feel fluid without restructuring pages.
/// <list type="bullet">
/// <item><c>Fx.Reveal="True"</c> gives an element a short fade + rise + settle whenever it
///   becomes visible, turning IsVisible-flip tab switching into a smooth cross-fade.</item>
/// <item><c>Fx.HoverLift="True"</c> animates a subtle scale on pointer hover. MAUI's
///   VisualStateManager changes state instantly (no transition), which is the main source of
///   "janky" hover feedback; this animates it on a timer so rows/cards lift smoothly. It is a
///   no-op on platforms without a pointer (touch), where hover doesn't apply.</item>
/// </list>
/// </summary>
public static class Fx
{
    // Snappy beats smooth here: a short fade + tiny rise reads as instant-but-soft. The
    // previous reveal added a simultaneous scale tween (which forces a relayout) over 210ms,
    // which made every tab/panel switch feel sluggish. Fade+translate only, ~120ms.
    private const uint RevealMs = 120;
    private const uint HoverMs = 120;
    private const double HoverScale = 1.015;

    // Remembers the recognizer we attached to a view, so toggling HoverLift removes only ours.
    private static readonly BindableProperty HoverRecognizerProperty = BindableProperty.CreateAttached(
        "HoverRecognizer", typeof(PointerGestureRecognizer), typeof(Fx), null);

    // ---------- Reveal ----------

    public static readonly BindableProperty RevealProperty = BindableProperty.CreateAttached(
        "Reveal", typeof(bool), typeof(Fx), false, propertyChanged: OnRevealChanged);

    public static bool GetReveal(BindableObject view) => (bool)view.GetValue(RevealProperty);

    public static void SetReveal(BindableObject view, bool value) => view.SetValue(RevealProperty, value);

    private static void OnRevealChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element) return;
        element.PropertyChanged -= OnElementPropertyChanged;
        if (newValue is true)
        {
            element.PropertyChanged += OnElementPropertyChanged;
        }
    }

    private static void OnElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VisualElement.IsVisible)) return;
        if (sender is not VisualElement element || !element.IsVisible) return;

        element.CancelAnimations();
        element.Opacity = 0;
        element.TranslationY = 6;
        element.Scale = 1; // no scale tween (it forces a relayout); keep reveals cheap
        // Quick fade + small rise: soft but effectively instant, so navigation stays snappy.
        _ = element.FadeToAsync(1, RevealMs, Easing.CubicOut);
        _ = element.TranslateToAsync(0, 0, RevealMs, Easing.CubicOut);
    }

    // ---------- HoverLift ----------

    public static readonly BindableProperty HoverLiftProperty = BindableProperty.CreateAttached(
        "HoverLift", typeof(bool), typeof(Fx), false, propertyChanged: OnHoverLiftChanged);

    public static bool GetHoverLift(BindableObject view) => (bool)view.GetValue(HoverLiftProperty);

    public static void SetHoverLift(BindableObject view, bool value) => view.SetValue(HoverLiftProperty, value);

    private static void OnHoverLiftChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view) return;

        // Remove any recognizer we previously attached to this view.
        if (view.GetValue(HoverRecognizerProperty) is PointerGestureRecognizer existing)
        {
            view.GestureRecognizers.Remove(existing);
            view.SetValue(HoverRecognizerProperty, null);
        }

        if (newValue is not true) return;

        var recognizer = new PointerGestureRecognizer();
        // ScaleToAsync shares one animation handle, so re-entering cancels the exit tween and
        // vice-versa - hover stays smooth even on fast pointer moves.
        recognizer.PointerEntered += (_, _) => _ = view.ScaleToAsync(HoverScale, HoverMs, Easing.CubicOut);
        recognizer.PointerExited += (_, _) => _ = view.ScaleToAsync(1.0, HoverMs, Easing.CubicOut);
        view.GestureRecognizers.Add(recognizer);
        view.SetValue(HoverRecognizerProperty, recognizer);
    }
}
