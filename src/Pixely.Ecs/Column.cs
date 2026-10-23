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

    public abstract void AddChunk();

    // Copies the row to the destination row of a column of the same type, then moves the last row into its place.
    public abstract void MoveRow(int row, int lastRow, Column destination, int destinationRow);

    // Moves the last row into the row and clears the last row, so the column stops referencing what it held.
    public abstract void RemoveRow(int row, int lastRow);

    // Clears the rows when they can hold references, so the column stops referencing what they held.
    public abstract void ClearRows(int first, int count);
}

// Stores rows in chunks of 1 << chunkShift, so it grows without copying and a row is a shift and a mask away.
// The archetype counts the rows and adds the chunks; rows passed to the unchecked members lie in an added chunk.
internal sealed class Column<T> : Column where T : struct
{
    private readonly int _shift;
    private T[][] _chunks = new T[4][];
    private int _chunkCount;

    public Column(int chunkShift)
    {
        _shift = chunkShift;
    }

    public override ColumnType Type => ColumnType<T>.Instance;

    private int Mask => (1 << _shift) - 1;

    // The first count rows of a chunk.
    public Span<T> GetChunk(int chunk, int count) => _chunks[chunk].AsSpan(0, count);

    // Count rows from row on, all in row's chunk.
    public Span<T> GetRows(int row, int count) => _chunks[row >> _shift].AsSpan(row & Mask, count);

    public ref T GetUnchecked(int row)
    {
        Debug.Assert((uint)row < (uint)(_chunkCount << _shift));
        T[] chunk = Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_chunks), row >> _shift);
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(chunk), row & Mask);
    }

    public override void AddChunk()
    {
        if (_chunkCount == _chunks.Length)
        {
            Array.Resize(ref _chunks, _chunks.Length * 2);
        }

        _chunks[_chunkCount++] = new T[1 << _shift];
    }

    public override void MoveRow(int row, int lastRow, Column destination, int destinationRow)
    {
        Debug.Assert(destination is Column<T>);
        Unsafe.As<Column<T>>(destination).GetUnchecked(destinationRow) = GetUnchecked(row);
        RemoveRow(row, lastRow);
    }

    public override void RemoveRow(int row, int lastRow)
    {
        ref T last = ref GetUnchecked(lastRow);
        GetUnchecked(row) = last;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            last = default;
        }
    }

    public override void ClearRows(int first, int count)
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            for (int row = first; row < first + count; row++)
            {
                GetUnchecked(row) = default;
            }
        }
    }
}
