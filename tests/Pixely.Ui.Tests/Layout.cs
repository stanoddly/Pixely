namespace Pixely.Ui.Tests;

/// <summary>
/// Drives a measure and arrange pass the way a root will, so tests exercise the real entry points.
/// </summary>
internal static class Layout
{
    public static Vector2Int Measure(Element root, Constraints constraints) => root.Measure(constraints);

    public static Vector2Int Measure(Element root, int maxWidth, int maxHeight) =>
        root.Measure(Constraints.Loose(maxWidth, maxHeight));

    public static Vector2Int MeasureUnbounded(Element root) => root.Measure(Constraints.Unconstrained);

    /// <summary>Measures the root into a viewport-sized box and arranges it there, clipped to the viewport.</summary>
    public static void Run(Element root, int width, int height)
    {
        Constraints viewport = Constraints.Tight(new Vector2Int(width, height));
        root.Measure(viewport);
        root.Arrange(new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height));
    }

    public static void Run(Element root, Constraints constraints, Rectangle bounds)
    {
        root.Measure(constraints);
        root.Arrange(bounds, bounds);
    }
}
