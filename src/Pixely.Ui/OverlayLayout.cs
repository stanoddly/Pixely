namespace Pixely.Ui;

/// <summary>
/// Stacks every child in the same space, sized to the largest of them. Has no main axis, so a
/// Grow child fills the axis it grows on rather than sharing a remainder with its siblings.
/// </summary>
public sealed class OverlayLayout : ILayout
{
    public static OverlayLayout Instance { get; } = new();

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
            host.ArrangeChild(i, contentBounds);
        }
    }
}
