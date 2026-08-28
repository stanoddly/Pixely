using System.Collections;
using Pixely.Collections;

namespace Pixely;

public sealed class BitMask
{
    private readonly BitSetStruct _bits;

    public Size<uint> Size { get; }

    public BitMask(Size<uint> size, bool initialValue = false)
    {
        Size = size;
        int length = GetLength(size);
        _bits = new BitSetStruct((nuint)length);

        if (initialValue)
        {
            for (int i = 0; i < length; i++)
            {
                _bits[(nuint)i] = true;
            }
        }
    }

    public BitMask(Size<uint> size, BitArray bits)
    {
        ArgumentNullException.ThrowIfNull(bits);

        Size = size;
        int length = GetLength(size);
        if (bits.Length != length)
        {
            throw new ArgumentException($"Bit count {bits.Length} does not match mask size {size.Width}x{size.Height}.", nameof(bits));
        }

        _bits = new BitSetStruct((nuint)length);
        for (int i = 0; i < length; i++)
        {
            _bits[(nuint)i] = bits[i];
        }
    }

    public bool this[int x, int y]
    {
        get => _bits[(nuint)GetIndex(x, y)];
        set => _bits[(nuint)GetIndex(x, y)] = value;
    }

    internal bool GetUnchecked(int index)
    {
        return _bits[(nuint)index];
    }

    private int GetIndex(int x, int y)
    {
        if (x < 0 || (uint)x >= Size.Width)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        if (y < 0 || (uint)y >= Size.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        return checked(y * (int)Size.Width + x);
    }

    private static int GetLength(Size<uint> size)
    {
        if (size.Width > int.MaxValue || size.Height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Mask dimensions cannot exceed Int32.MaxValue.");
        }

        ulong length = (ulong)size.Width * size.Height;
        if (length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Mask bit count cannot exceed Int32.MaxValue.");
        }

        return (int)length;
    }
}
