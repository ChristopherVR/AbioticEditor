namespace AbioticEditor.App.Controls;

/// <summary>
/// Attached helper that recomputes a CollectionView's GridItemsLayout span from the
/// view's measured width, so slot grids show as many columns as fit instead of a
/// hardcoded span (6 columns on desktop squeezes into unreadable slivers on a phone).
/// Usage: controls:ResponsiveLayout.ItemWidth="104" on a CollectionView whose
/// ItemsLayout is a vertical GridItemsLayout.
/// </summary>
public static class ResponsiveLayout
{
    public static readonly BindableProperty ItemWidthProperty = BindableProperty.CreateAttached(
        "ItemWidth", typeof(double), typeof(ResponsiveLayout), 0.0, propertyChanged: OnItemWidthChanged);

    public static double GetItemWidth(BindableObject view) => (double)view.GetValue(ItemWidthProperty);

    public static void SetItemWidth(BindableObject view, double value) => view.SetValue(ItemWidthProperty, value);

    public static readonly BindableProperty MaxSpanProperty = BindableProperty.CreateAttached(
        "MaxSpan", typeof(int), typeof(ResponsiveLayout), 12);

    public static int GetMaxSpan(BindableObject view) => (int)view.GetValue(MaxSpanProperty);

    public static void SetMaxSpan(BindableObject view, int value) => view.SetValue(MaxSpanProperty, value);

    private static void OnItemWidthChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not CollectionView cv) return;
        cv.SizeChanged -= OnSizeChanged;
        if ((double)newValue > 0)
        {
            cv.SizeChanged += OnSizeChanged;
            Apply(cv);
        }
    }

    private static void OnSizeChanged(object? sender, EventArgs e)
    {
        if (sender is CollectionView cv) Apply(cv);
    }

    private static void Apply(CollectionView cv)
    {
        var width = cv.Width;
        var itemWidth = GetItemWidth(cv);
        if (width <= 0 || itemWidth <= 0) return;
        if (cv.ItemsLayout is not GridItemsLayout grid || grid.Orientation != ItemsLayoutOrientation.Vertical) return;

        var spacing = grid.HorizontalItemSpacing;
        var span = (int)((width + spacing) / (itemWidth + spacing));
        span = Math.Clamp(span, 1, GetMaxSpan(cv));
        if (grid.Span != span) grid.Span = span;
    }
}
