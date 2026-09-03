using System.Numerics;

namespace Pixely;

public readonly record struct Rectangle(int X, int Y, int Width, int Height)
{
    public Rectangle(Vector2Int position, Vector2Int size) : this(position.X, position.Y, size.X, size.Y) { }
    public (int, int) GetXY() => (X, Y);
    public (int, int) GetSize() => (Width, Height);

    public bool Intersects(Vector2Int point) => point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public Rectangle Intersect(Rectangle other)
    {
        int left = Math.Max(X, other.X);
        int top = Math.Max(Y, other.Y);
        int right = Math.Min(X + Width, other.X + other.Width);
        int bottom = Math.Min(Y + Height, other.Y + other.Height);

        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
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

    public bool Intersects(ShortVector2 point) => point.X >= X && point.X < X + Width && point.Y >= Y && point.Y < Y + Height;

    public ShortRectangle Offset(ShortVector2 offset) =>
        new((short)(X + offset.X), (short)(Y + offset.Y), Width, Height);
}
