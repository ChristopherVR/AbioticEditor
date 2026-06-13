namespace AbioticEditor.App.Controls;

/// <summary>
/// A Grid that collapses its cell layout into a single vertical stack when its own
/// width drops below <see cref="CompactWidth"/> (phone / narrow window). Children keep
/// their declared order (row-major). While stacked, fixed child widths are lifted so
/// panes stretch edge to edge; if the grid itself had a fixed height (the master-detail
/// panes do) that height is cleared and moved onto each height-less child so the inner
/// lists keep a scrollable viewport instead of collapsing to zero.
/// </summary>
public sealed class AdaptiveGrid : Grid
{
    public static readonly BindableProperty CompactWidthProperty = BindableProperty.Create(
        nameof(CompactWidth), typeof(double), typeof(AdaptiveGrid), 720.0);

    public static readonly BindableProperty StackedChildHeightProperty = BindableProperty.Create(
        nameof(StackedChildHeight), typeof(double), typeof(AdaptiveGrid), 380.0);

    /// <summary>Below this measured width the grid stacks its children vertically.</summary>
    public double CompactWidth
    {
        get => (double)GetValue(CompactWidthProperty);
        set => SetValue(CompactWidthProperty, value);
    }

    /// <summary>
    /// Height given, while stacked, to children that declare none — only applied when
    /// the grid itself had a fixed height (i.e. children were sized by the grid).
    /// </summary>
    public double StackedChildHeight
    {
        get => (double)GetValue(StackedChildHeightProperty);
        set => SetValue(StackedChildHeightProperty, value);
    }

    private bool _stacked;
    private ColumnDefinitionCollection? _savedColumns;
    private RowDefinitionCollection? _savedRows;
    private double _savedHeightRequest = -1;
    private readonly List<(IView Child, int Row, int Column, int RowSpan, int ColumnSpan, double WidthRequest, double HeightRequest)> _savedChildren = [];

    public AdaptiveGrid()
    {
        SizeChanged += (_, _) => ApplyForWidth(Width);
    }

    private void ApplyForWidth(double width)
    {
        if (width <= 0) return;
        var wantStacked = width < CompactWidth;
        if (wantStacked == _stacked) return;

        if (wantStacked) Stack();
        else Restore();
    }

    private void Stack()
    {
        _stacked = true;

        _savedColumns = ColumnDefinitions;
        _savedRows = RowDefinitions;
        _savedHeightRequest = HeightRequest;
        _savedChildren.Clear();

        var ordered = Children
            .Select(c => (Child: c,
                Row: c is BindableObject b ? GetRow(b) : 0,
                Column: c is BindableObject b2 ? GetColumn(b2) : 0))
            .OrderBy(t => t.Row).ThenBy(t => t.Column)
            .Select(t => t.Child)
            .ToList();

        foreach (var child in ordered)
        {
            if (child is not BindableObject bo) continue;
            var ve = child as VisualElement;
            _savedChildren.Add((child, GetRow(bo), GetColumn(bo), GetRowSpan(bo), GetColumnSpan(bo),
                ve?.WidthRequest ?? -1, ve?.HeightRequest ?? -1));
        }

        var rows = new RowDefinitionCollection();
        foreach (var _ in ordered) rows.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions = rows;
        ColumnDefinitions = [new ColumnDefinition(GridLength.Star)];
        if (RowSpacing <= 0) RowSpacing = Math.Max(ColumnSpacing, 8);

        var giveChildrenHeight = _savedHeightRequest > 0;
        if (giveChildrenHeight) HeightRequest = -1;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i] is not BindableObject bo) continue;
            SetRow(bo, i);
            SetColumn(bo, 0);
            SetRowSpan(bo, 1);
            SetColumnSpan(bo, 1);
            if (ordered[i] is VisualElement ve)
            {
                ve.WidthRequest = -1;
                if (giveChildrenHeight && ve.HeightRequest < 0)
                {
                    ve.HeightRequest = StackedChildHeight;
                }
            }
        }
    }

    private void Restore()
    {
        _stacked = false;

        foreach (var (child, row, column, rowSpan, columnSpan, widthRequest, heightRequest) in _savedChildren)
        {
            if (child is not BindableObject bo) continue;
            SetRow(bo, row);
            SetColumn(bo, column);
            SetRowSpan(bo, rowSpan);
            SetColumnSpan(bo, columnSpan);
            if (child is VisualElement ve)
            {
                ve.WidthRequest = widthRequest;
                ve.HeightRequest = heightRequest;
            }
        }
        _savedChildren.Clear();

        if (_savedColumns is not null) ColumnDefinitions = _savedColumns;
        if (_savedRows is not null) RowDefinitions = _savedRows;
        HeightRequest = _savedHeightRequest;
    }
}
