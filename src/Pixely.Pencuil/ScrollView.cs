namespace Pixely.Pencuil;

/// <summary>
/// Scope produced by <see cref="ScrollViewExtensions.ScrollView"/>. Content built inside it is
/// clipped to the viewport and shifted by the scroll offset; the scrollbar is drawn on disposal
/// so it stays above the content.
/// </summary>
public ref struct ScrollViewDisposer : IDisposable
{
    private readonly Pencil _pencil;
    private readonly ref int _offset;
    private readonly int _id;
    private readonly int _contentExtent;
    private readonly int _viewportExtent;
    private readonly Rectangle _barArea;
    private readonly Rectangle _viewArea;
    private readonly Orientation _orientation;
    private readonly Rectangle? _previousClip;
    private readonly LayoutDirection _previousDirection;
    private readonly int _previousGap;

    internal ScrollViewDisposer(
        Pencil pencil,
        ref int offset,
        int id,
        int contentExtent,
        int viewportExtent,
        Rectangle barArea,
        Rectangle viewArea,
        Orientation orientation,
        Rectangle? previousClip,
        LayoutDirection previousDirection,
        int previousGap)
    {
        _pencil = pencil;
        _offset = ref offset;
        _id = id;
        _contentExtent = contentExtent;
        _viewportExtent = viewportExtent;
        _barArea = barArea;
        _viewArea = viewArea;
        _orientation = orientation;
        _previousClip = previousClip;
        _previousDirection = previousDirection;
        _previousGap = previousGap;
    }

    public void Dispose()
    {
        _pencil.CurrentClip = _previousClip;
        _pencil.CurrentDirection = _previousDirection;
        _pencil.CurrentGap = _previousGap;

        _pencil.MoveTo(_barArea.X, _barArea.Y);
        ScrollBarExtensions.ScrollBarCore(_pencil, _id, ref _offset, _contentExtent, _viewportExtent, _barArea, _orientation);

        // Leave the layout cursor as if the whole view had been placed as one element
        _pencil.CurrentSize = new Vector2Int(_viewArea.Width, _viewArea.Height);
        _pencil.MoveTo(_viewArea.X, _viewArea.Y);
        _pencil.CurrentPosition = _pencil.DetermineNextPosition(_pencil.CurrentSize);
    }
}

public static class ScrollViewExtensions
{
    /// <summary>
    /// Opens a scrollable region of <paramref name="width"/> by <paramref name="height"/> holding
    /// content of <paramref name="contentExtent"/> pixels along the scrolling axis. Content built
    /// inside the scope is clipped and offset; a scrollbar is reserved along the trailing edge.
    /// </summary>
    public static ScrollViewDisposer ScrollView(
        this Pencil pencil,
        int id,
        int width,
        int height,
        ref int offset,
        int contentExtent,
        Orientation orientation = Orientation.Vertical)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        GuiStyle style = pencil.Style;
        int thickness = style.ScrollBarThickness;
        Rectangle viewArea = new Rectangle(pencil.CurrentPosition, new Vector2Int(width, height));

        Rectangle contentArea;
        Rectangle barArea;
        if (orientation == Orientation.Vertical)
        {
            contentArea = new Rectangle(viewArea.X, viewArea.Y, Math.Max(0, width - thickness), height);
            barArea = new Rectangle(viewArea.X + contentArea.Width, viewArea.Y, thickness, height);
        }
        else
        {
            contentArea = new Rectangle(viewArea.X, viewArea.Y, width, Math.Max(0, height - thickness));
            barArea = new Rectangle(viewArea.X, viewArea.Y + contentArea.Height, width, thickness);
        }

        int viewportExtent = orientation == Orientation.Vertical ? contentArea.Height : contentArea.Width;

        // Resolve the wheel before building so the content is laid out at its final offset
        offset = ScrollBarGeometry.ClampOffset(offset, contentExtent, viewportExtent);
        offset = ScrollBarExtensions.ApplyWheel(pencil, offset, contentExtent, viewportExtent, contentArea, orientation);

        pencil.AddScrollArea(contentArea);

        Rectangle? previousClip = pencil.CurrentClip;
        LayoutDirection previousDirection = pencil.CurrentDirection;
        int previousGap = pencil.CurrentGap;

        pencil.CurrentClip = previousClip == null ? contentArea : previousClip.Value.Intersect(contentArea);
        pencil.CurrentDirection = orientation == Orientation.Vertical ? LayoutDirection.Bottom : LayoutDirection.Right;
        pencil.CurrentSize = default;
        pencil.MoveTo(
            orientation == Orientation.Vertical ? contentArea.X : contentArea.X - offset,
            orientation == Orientation.Vertical ? contentArea.Y - offset : contentArea.Y);

        return new ScrollViewDisposer(
            pencil,
            ref offset,
            id,
            contentExtent,
            viewportExtent,
            barArea,
            viewArea,
            orientation,
            previousClip,
            previousDirection,
            previousGap);
    }
}
