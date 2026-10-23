using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

internal abstract class Column
{
    public abstract Column CreateEmpty();

    public abstract void Grow(int count);

    // Appends the row to the destination, which holds the same component type, and swap-removes it here.
    public abstract void MoveRow(int row, Column destination);

    public abstract void RemoveRow(int row);
}

// Owns its array, so a row the archetype's records point at is read without a bounds check.
internal sealed class Column<T> : Column where T : struct
{
    private T[] _values = new T[64];
    private int _count;

    public Span<T> AsSpan() => _values.AsSpan(0, _count);

    // The row must be below the column's count, which the world's records guarantee.
    public ref T GetUnchecked(int row)
    {
        Debug.Assert((uint)row < (uint)_count);
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_values), row);
    }

    public void Add(in T value)
    {
        if (_count == _values.Length)
        {
            Array.Resize(ref _values, _values.Length * 2);
        }

        _values[_count++] = value;
    }

    public override Column CreateEmpty() => new Column<T>();

    public override void Grow(int count)
    {
        int length = _count + count;
        if (length > _values.Length)
        {
            Array.Resize(ref _values, Math.Max(length, _values.Length * 2));
        }

        _count = length;
    }

    public override void MoveRow(int row, Column destination)
    {
        ((Column<T>)destination).Add(_values[row]);
        RemoveRow(row);
    }

    // Clears the vacated last slot, so the column stops referencing what it held.
    public override void RemoveRow(int row)
    {
        int last = --_count;
        _values[row] = _values[last];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _values[last] = default;
        }
    }
}
