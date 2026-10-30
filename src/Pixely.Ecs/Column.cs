using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

// The component type of a column, which creates columns for new archetypes without a column to copy from.
internal abstract class ColumnType
{
    public abstract int Size { get; }

    // A tag has no data, so an archetype keeps no column for it.
    public abstract bool IsTag { get; }

    // How the world indexes the component, or null when it is no relationship.
    public abstract RelationshipType? Relationship { get; }

    public abstract Column Create(int capacity);
}

internal sealed class ColumnType<T> : ColumnType where T : struct
{
    public static ColumnType<T> Instance { get; } = new ColumnType<T>();

    public override int Size => Unsafe.SizeOf<T>();

    public override bool IsTag => default(T) is ITag;

    // Throws for a relationship the world could not index: one the generator did not register, or a tag, which has no column to read its target from.
    public override RelationshipType? Relationship => default(T) is IRelationship
        ? default(T) is ITag
            ? throw new InvalidOperationException($"The relationship {ComponentType<T>.Name} is a tag, which has no data to hold its target.")
            : RelationshipType<T>.Instance ?? throw new InvalidOperationException(
                $"The relationship {ComponentType<T>.Name} is not registered. The Pixely.Ecs generator registers every struct that implements IRelationship; reference it as an analyzer.")
        : null;

    public override Column Create(int capacity) => new Column<T>(capacity);
}

internal abstract class Column
{
    // The T[] of a Column<T>, which Grow replaces.
    public abstract object Items { get; }

    // Replaces the rows with an array of the capacity that holds the first count of them.
    public abstract void Grow(int capacity, int count);

    // Copies the row to the destination row of a column of the same type, then moves the last row into its place.
    public abstract void MoveRow(int row, int lastRow, Column destination, int destinationRow);

    // Moves the last row into the row and clears the last row, so the column stops referencing what it held.
    public abstract void RemoveRow(int row, int lastRow);
}

// Stores rows in one array, which Grow replaces with a larger one. The archetype counts the rows and grows the columns together; rows passed to
// the unchecked members lie below the capacity.
internal sealed class Column<T> : Column where T : struct
{
    private T[] _items;

    public Column(int capacity)
    {
        _items = new T[capacity];
    }

    public override object Items => _items;

    // Count rows from row on.
    public Span<T> GetRows(int row, int count) => _items.AsSpan(row, count);

    public ref T GetUnchecked(int row)
    {
        Debug.Assert((uint)row < (uint)_items.Length);
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_items), row);
    }

    public override void Grow(int capacity, int count)
    {
        T[] items = new T[capacity];
        _items.AsSpan(0, count).CopyTo(items);
        _items = items;
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
}
