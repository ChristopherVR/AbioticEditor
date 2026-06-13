namespace AbioticEditor.App.Controls;

/// <summary>
/// A tab/panel host that defers building its content until it is first activated, then keeps
/// it for instant re-show.
///
/// <para>
/// The editor's tab strips previously instantiated EVERY tab's view up front (11 player tabs,
/// 8 world tabs) and merely toggled <c>IsVisible</c>. That kept the entire visual tree for
/// every tab live at all times - so every layout pass (scroll, resize, even a keystroke that
/// remeasures) and every binding update traversed all of them, which is the main reason the UI
/// felt heavy. Wrapping each tab in a <see cref="LazyView"/> means a tab's view is created only
/// when the user first opens it; tabs never visited cost nothing, and the live tree is just the
/// active tab (plus any previously opened ones).
/// </para>
///
/// <para>Usage:
/// <code>
/// &lt;controls:LazyView IsActive="{Binding PlayerEditor.IsVitalsTab}"&gt;
///   &lt;controls:LazyView.ContentTemplate&gt;
///     &lt;DataTemplate&gt;&lt;player:PlayerVitalsTab/&gt;&lt;/DataTemplate&gt;
///   &lt;/controls:LazyView.ContentTemplate&gt;
/// &lt;/controls:LazyView&gt;
/// </code>
/// </para>
/// </summary>
public sealed class LazyView : ContentView
{
    private bool _created;

    public LazyView()
    {
        // Collapsed until first activated, so an unopened tab contributes no layout.
        IsVisible = false;
    }

    /// <summary>Whether this view's tab is the selected one. Drives lazy creation + visibility.</summary>
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(
        nameof(IsActive), typeof(bool), typeof(LazyView), false, propertyChanged: OnIsActiveChanged);

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>The template built on first activation. Its root is the tab's real view.</summary>
    public static readonly BindableProperty ContentTemplateProperty = BindableProperty.Create(
        nameof(ContentTemplate), typeof(DataTemplate), typeof(LazyView));

    public DataTemplate? ContentTemplate
    {
        get => (DataTemplate?)GetValue(ContentTemplateProperty);
        set => SetValue(ContentTemplateProperty, value);
    }

    private static void OnIsActiveChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not LazyView view)
        {
            return;
        }
        var active = (bool)newValue;

        if (active && !view._created && view.ContentTemplate is { } template)
        {
            view._created = true;
            // CreateContent instantiates the template's root (the tab ContentView). It inherits
            // this LazyView's BindingContext, so the tab's compiled bindings resolve as before.
            if (template.CreateContent() is View built)
            {
                view.Content = built;
            }
        }

        view.IsVisible = active;
    }
}
