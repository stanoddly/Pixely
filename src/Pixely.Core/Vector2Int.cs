using System.Numerics;

namespace Pixely;

public readonly record struct Vector2Int(int X, int Y)
{
    public Vector2Int(int value) : this(value, value)
    {
    }

    public static implicit operator Vector2(Vector2Int iv) => new Vector2(iv.X, iv.Y);

    public static implicit operator Vector2Int((int x, int y) position) => new Vector2Int(position.x, position.y);

    public static explicit operator Vector2Int(Vector2 v) => new Vector2Int((int)v.X, (int)v.Y);

    public static Vector2Int operator +(Vector2Int left, Vector2Int right) => new Vector2Int(left.X + right.X, left.Y + right.Y);

    public static Vector2Int operator -(Vector2Int left, Vector2Int right) => new Vector2Int(left.X - right.X, left.Y - right.Y);

    public static Vector2Int operator *(Vector2Int left, int right) => new Vector2Int(left.X * right, left.Y * right);

    public static Vector2Int Zero { get; } = default;

    public override string ToString() => $"({X}, {Y})";
}
