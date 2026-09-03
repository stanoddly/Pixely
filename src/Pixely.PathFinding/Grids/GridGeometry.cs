namespace Pixely.PathFinding.Grids;

/// <summary>
/// Maps zero-based row-major grid positions to dense node indices and back.
/// </summary>
/// <remarks>
/// The origin is always <c>(0, 0)</c> and positions run from there toward increasing x and y.
/// A consumer whose world is centred, offset or measured in world units applies that transform itself.
/// </remarks>
public readonly struct GridGeometry
{
    public GridGeometry(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        NodeCount = checked(width * height);
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public int NodeCount { get; }

    public bool Contains(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    public int GetIndex(int x, int y)
    {
        if (!Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(x < 0 || x >= Width ? nameof(x) : nameof(y), $"The position ({x}, {y}) is outside the {Width}x{Height} grid.");
        }

        return y * Width + x;
    }

    public bool TryGetIndex(int x, int y, out int index)
    {
        if (!Contains(x, y))
        {
            index = -1;
            return false;
        }

        index = y * Width + x;
        return true;
    }

    public (int X, int Y) GetPosition(int index)
    {
        if ((uint)index >= (uint)NodeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"The index is outside the {Width}x{Height} grid.");
        }

        return (index % Width, index / Width);
    }
}
