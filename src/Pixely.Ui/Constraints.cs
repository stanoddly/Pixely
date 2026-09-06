namespace Pixely.Ui;

/// <summary>
/// The size range an element may take. An axis is <em>definite</em> when its minimum equals its
/// maximum: the parent has committed to an exact extent, which is what Grow and Percent need.
/// Encoding definiteness as a tight range rather than a separate flag keeps it inside the
/// measure cache key.
/// </summary>
public readonly record struct Constraints(int MinWidth, int MinHeight, int MaxWidth, int MaxHeight)
{
    /// <summary>A maximum of this value means "as much as you want".</summary>
    public const int Unbounded = int.MaxValue;

    public Constraints(int maxWidth, int maxHeight) : this(0, 0, maxWidth, maxHeight)
    {
    }

    public static Constraints Unconstrained => new(0, 0, Unbounded, Unbounded);

    public static Constraints Tight(Vector2Int size) => new(size.X, size.Y, size.X, size.Y);

    public static Constraints Loose(int maxWidth, int maxHeight) => new(0, 0, maxWidth, maxHeight);

    public bool IsWidthDefinite => MinWidth == MaxWidth;
    public bool IsHeightDefinite => MinHeight == MaxHeight;

    public bool IsWidthBounded => MaxWidth != Unbounded;
    public bool IsHeightBounded => MaxHeight != Unbounded;

    public bool IsAxisDefinite(Orientation orientation) =>
        orientation == Orientation.Horizontal ? IsWidthDefinite : IsHeightDefinite;

    public Constraints WithUnboundedWidth() => this with { MinWidth = 0, MaxWidth = Unbounded };
    public Constraints WithUnboundedHeight() => this with { MinHeight = 0, MaxHeight = Unbounded };

    public Constraints WithTightWidth(int width) => this with { MinWidth = width, MaxWidth = width };
    public Constraints WithTightHeight(int height) => this with { MinHeight = height, MaxHeight = height };

    /// <summary>
    /// Removes <paramref name="thickness"/> from both bounds, keeping an unbounded maximum unbounded.
    /// </summary>
    public Constraints Deflate(Thickness thickness)
    {
        return new Constraints(
            Math.Max(0, MinWidth - thickness.Horizontal),
            Math.Max(0, MinHeight - thickness.Vertical),
            ReduceMax(MaxWidth, thickness.Horizontal),
            ReduceMax(MaxHeight, thickness.Vertical));
    }

    /// <summary>
    /// Clamps to zero first, then applies the minimum, then the maximum, so a positive minimum
    /// always wins over the zero clamp.
    /// </summary>
    public Vector2Int Clamp(Vector2Int size)
    {
        return new Vector2Int(
            ClampAxis(size.X, MinWidth, MaxWidth),
            ClampAxis(size.Y, MinHeight, MaxHeight));
    }

    private static int ClampAxis(int value, int min, int max)
    {
        int clamped = Math.Max(0, value);
        clamped = Math.Max(clamped, min);
        return max == Unbounded ? clamped : Math.Min(clamped, max);
    }

    private static int ReduceMax(int max, int amount)
    {
        if (max == Unbounded)
        {
            return Unbounded;
        }

        return Math.Max(0, max - amount);
    }

    public override string ToString() => $"[{MinWidth}..{DescribeMax(MaxWidth)} x {MinHeight}..{DescribeMax(MaxHeight)}]";

    private static string DescribeMax(int max) => max == Unbounded ? "inf" : max.ToString();
}
