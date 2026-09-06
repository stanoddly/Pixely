namespace Pixely.Ui;

/// <summary>
/// Places each child at its own <see cref="Element.Anchor"/> and keeps it inside the content, which
/// is what a popup needs: a menu pinned to something in the world, a tooltip following the pointer.
/// </summary>
/// <remarks>
/// <para>
/// The clamping is the reason this is a layout rather than an <see cref="Element.Offset"/> the
/// caller computes. A caller can only clamp against a size it already knows, and it does not know
/// one until the element has been measured — which is why the immediate-mode equivalent has to
/// measure its own text by hand. Here the placement happens after measure, so the size is simply
/// available, and a viewport that changes re-clamps by arranging again.
/// </para>
/// <para>
/// A pivot per axis because an anchor names a point, and the element still has to be told which of
/// its own edges meets it. <see cref="Alignment.Stretch"/> reads as <see cref="Alignment.Start"/>:
/// there is no extent to fill against a point.
/// </para>
/// </remarks>
public sealed class AnchoredLayout : ILayout
{
    /// <summary>Anchors the top-left corner, which is the common case for a menu opening down and right.</summary>
    public static AnchoredLayout TopLeft { get; } = new();

    public AnchoredLayout(Alignment horizontalPivot = Alignment.Start, Alignment verticalPivot = Alignment.Start)
    {
        HorizontalPivot = horizontalPivot;
        VerticalPivot = verticalPivot;
    }

    public Alignment HorizontalPivot { get; }

    public Alignment VerticalPivot { get; }

    /// <summary>
    /// The extent of the largest child, ignoring where any of them is anchored. An anchor places an
    /// element without resizing what holds it, exactly as an offset does, so a host that fits its
    /// children hugs their sizes rather than the region they were scattered across.
    /// </summary>
    public Vector2Int MeasureChildren(ILayoutHost host, Constraints contentConstraints)
    {
        int width = 0;
        int height = 0;

        for (int i = 0; i < host.ChildCount; i++)
        {
            Vector2Int size = host.MeasureChild(i, contentConstraints);
            width = Math.Max(width, size.X);
            height = Math.Max(height, size.Y);
        }

        return new Vector2Int(width, height);
    }

    public void ArrangeChildren(ILayoutHost host, Rectangle contentBounds)
    {
        for (int i = 0; i < host.ChildCount; i++)
        {
            Element child = host.GetChild(i);
            Thickness margin = child.Margin;

            // Everything is decided on the margin box and converted back at the end. A margin is
            // space the element asked to keep clear, so clamping the border box would push that
            // space off screen and call the result on screen.
            Vector2Int size = new(child.DesiredSize.X + margin.Horizontal, child.DesiredSize.Y + margin.Vertical);

            int x = Place(child.Anchor.X + child.Offset.X, size.X, HorizontalPivot, contentBounds.X, contentBounds.Width);
            int y = Place(child.Anchor.Y + child.Offset.Y, size.Y, VerticalPivot, contentBounds.Y, contentBounds.Height);

            host.ArrangeChildAt(i, new Rectangle(x + margin.Left, y + margin.Top, child.DesiredSize.X, child.DesiredSize.Y));
        }
    }

    /// <summary>
    /// Puts <paramref name="extent"/> against <paramref name="anchor"/> by the pivot, then moves it
    /// back inside the content. An element larger than the content is pinned to the leading edge
    /// rather than clamped, because there is no position that fits and the leading edge is the one
    /// whose content a reader starts from.
    /// </summary>
    private static int Place(int anchor, int extent, Alignment pivot, int contentStart, int contentExtent)
    {
        int position = pivot switch
        {
            Alignment.Center => anchor - extent / 2,
            Alignment.End => anchor - extent,
            _ => anchor
        };

        return Math.Clamp(position, contentStart, contentStart + Math.Max(0, contentExtent - extent));
    }
}
