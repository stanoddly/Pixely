using System.Numerics;

namespace Pixely;

public readonly record struct Rectangle(int X, int Y, int Width, int Height)
{
    public Rectangle(Vector2Int position, Vector2Int size) : this(position.X, position.Y, size.X, size.Y) { }
    public (int, int) GetXY() => (X, Y);
    public (int, int) GetSize() => (Width, Height);

    public bool Intersects(Vector2Int point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;

    /// <summary>
    /// Whether <paramref name="point"/> is inside, with the right and bottom edges treated as
    /// outside. That makes the test half-open, unlike the inclusive <see cref="Intersects"/>, so
    /// two adjacent rectangles never both claim the pixel they share.
    /// </summary>
    public bool Contains(Vector2Int point)
    {
        return point.X >= X && point.Y >= Y && point.X - (long)X < Width && point.Y - (long)Y < Height;
    }

    /// <summary>
    /// The overlapping area of this rectangle and <paramref name="other"/>, or an empty rectangle
    /// when they do not overlap. Long arithmetic keeps a rectangle near <see cref="int.MaxValue"/>
    /// from wrapping into a valid-looking result.
    /// </summary>
    public Rectangle Intersect(Rectangle other)
    {
        long left = Math.Max(X, other.X);
        long top = Math.Max(Y, other.Y);
        long right = Math.Min((long)X + Width, (long)other.X + other.Width);
        long bottom = Math.Min((long)Y + Height, (long)other.Y + other.Height);

        if (right <= left || bottom <= top)
        {
            return default;
        }

        return new Rectangle((int)left, (int)top, (int)(right - left), (int)(bottom - top));
    }
}

public readonly record struct Rectangle<TType>(TType X, TType Y, TType Width, TType Height) where TType : unmanaged, INumberBase<TType>
{
    public (TType, TType) GetXY() => (X, Y);
    public (TType, TType) GetSize() => (Width, Height);

    public Size<TType> Size => new(Width, Height);
}

//[JsonConverter(typeof(SizeJsonConverter))]
public readonly record struct ShortRectangle(short X, short Y, ushort Width, ushort Height)
{
    public ShortRectangle(ShortVector2 position, UShortVector2 size) : this(position.X, position.Y, size.X, size.Y) { }
    public ShortVector2 Position => new ShortVector2(X, Y);
    public UShortVector2 Size => new UShortVector2(Width, Height);

    public bool Intersects(ShortVector2 point) => point.X >= X && point.X <= X + Width && point.Y >= Y && point.Y <= Y + Height;

    public ShortRectangle Offset(ShortVector2 offset) =>
        new((short)(X + offset.X), (short)(Y + offset.Y), Width, Height);
}
