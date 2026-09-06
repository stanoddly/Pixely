namespace Pixely.Ui;

/// <summary>
/// Per-edge spacing, used for both <see cref="Element.Margin"/> and <see cref="Element.Padding"/>.
/// Negative values are allowed on margins and rejected on padding by the property that stores them.
/// </summary>
public readonly record struct Thickness(int Left, int Top, int Right, int Bottom)
{
    public Thickness(int all) : this(all, all, all, all)
    {
    }

    public Thickness(int horizontal, int vertical) : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public static Thickness Zero => default;

    public int Horizontal => Left + Right;
    public int Vertical => Top + Bottom;

    public bool HasNegativeEdge => Left < 0 || Top < 0 || Right < 0 || Bottom < 0;

    public Vector2Int Size => new(Horizontal, Vertical);

    public Rectangle Deflate(Rectangle rectangle)
    {
        return new Rectangle(
            rectangle.X + Left,
            rectangle.Y + Top,
            Math.Max(0, rectangle.Width - Horizontal),
            Math.Max(0, rectangle.Height - Vertical));
    }

    public override string ToString() => $"({Left}, {Top}, {Right}, {Bottom})";
}
