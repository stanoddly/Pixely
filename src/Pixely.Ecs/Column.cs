using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

// The component type of a column, which creates columns for new archetypes without a column to copy from.
internal abstract class ColumnType
{
    public abstract int Size { get; }

    public abstract Column Create(int chunkShift);
}

internal sealed class ColumnType<T> : ColumnType where T : struct
{
    public static ColumnType<T> Instance { get; } = new ColumnType<T>();

    public override int Size => Unsafe.SizeOf<T>();

    public override Column Create(int chunkShift) => new Column<T>(chunkShift);
}

internal abstract class Column
{
    public abstract ColumnType Type { get; }

    public abstract void Grow(int count);

    public abstract void Shrink(int count);

    // Appends the row to the destination, which holds the same component type, and swap-removes it here.
    public abstract void MoveRow(int row, Column destination);

    public abstract void RemoveRow(int row);
}

// Stores rows in chunks of 1 << chunkShift, so it grows without copying and a row is a shift and a mask away.
// Rows passed to the unchecked members must be below Count, which the world's records guarantee.
internal sealed class Column<T> : Column where T : struct
{
    private readonly int _shift;
    private T[][] _chunks = new T[4][];
    private int _chunkCount;

    public Column(int chunkShift)
    {
        _shift = chunkShift;
    }

    public int Count { get; private set; }

    public override ColumnType Type => ColumnType<T>.Instance;

    private int Mask => (1 << _shift) - 1;

    // The first count rows of a chunk.
    public Span<T> GetChunk(int chunk, int count) => _chunks[chunk].AsSpan(0, count);

    // Count rows from row on, all in row's chunk.
    public Span<T> GetRows(int row, int count) => _chunks[row >> _shift].AsSpan(row & Mask, count);

    public ref T GetUnchecked(int row)
    {
        Debug.Assert((uint)row < (uint)Count);
        T[] chunk = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_chunks), row >> _shift);
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(chunk), row & Mask);
    }

    public int Add(in T value)
    {
        int row = Count;
        EnsureCapacity(row + 1);
        Count = row + 1;
        GetUnchecked(row) = value;
        return row;
    }

    public override void Grow(int count)
    {
        EnsureCapacity(Count + count);
        Count += count;
    }

    // Drops the last count rows, clearing them so the column stops referencing what they held.
    public override void Shrink(int count)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            for (int row = Count - count; row < Count; row++)
            {
                GetUnchecked(row) = default;
            }
        }

        Count -= count;
    }

    public override void MoveRow(int row, Column destination)
    {
        Debug.Assert(destination is Column<T>);
        Unsafe.As<Column<T>>(destination).Add(GetUnchecked(row));
        RemoveRow(row);
    }

    // Clears the vacated last row, so the column stops referencing what it held.
    public override void RemoveRow(int row)
    {
        int last = Count - 1;
        GetUnchecked(row) = GetUnchecked(last);
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            GetUnchecked(last) = default;
        }

        Count = last;
    }

    private void EnsureCapacity(int rows)
    {
        while (_chunkCount << _shift < rows)
        {
            if (_chunkCount == _chunks.Length)
            {
                Array.Resize(ref _chunks, _chunks.Length * 2);
            }

            _chunks[_chunkCount++] = new T[1 << _shift];
        }
    }
}
