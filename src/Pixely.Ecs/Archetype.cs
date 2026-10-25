using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

// The entities that have exactly the same component types, with one column per component type that is not a tag, in the order of the set.
// Each column stores its rows in one array. The archetype counts the rows for all its columns and grows them together, doubling their capacity.
internal sealed class Archetype
{
    // What GetColumnIndex returns for a component the archetype lacks, and for a tag it has, which has no column.
    public const int Absent = -1;
    public const int Tag = -2;

    private const int InitialCapacity = 8;

    private readonly ColumnType[] _types;
    private readonly Column<Entity> _entities;
    private readonly Column[] _columns;
    private readonly int[] _columnComponentIds;

    // The column of each component id, Absent or Tag. Ids allocated after the archetype was created lie past its end.
    private readonly int[] _columnIndices;

    // The rows of each component id's column, as the T[] of its Column<T>, or null for a component the archetype lacks and for a tag.
    // Get reaches a row through them without loading the Column. Grow refreshes them, because a column replaces its T[] when it grows.
    private readonly object?[] _itemsById;

    // The edges reached by adding or removing each component id, or null until first used.
    private ArchetypeEdge?[] _addEdges = [];
    private ArchetypeEdge?[] _removeEdges = [];

    // The rows each column has room for.
    private int _capacity = InitialCapacity;

    // The types are in the order of the set's ids.
    public Archetype(ComponentSet components, ColumnType[] types)
    {
        Components = components;
        _types = types;

        int columnCount = types.Count(type => !type.IsTag);
        _entities = new Column<Entity>(InitialCapacity);
        _columns = new Column[columnCount];
        _columnComponentIds = new int[columnCount];
        _columnIndices = new int[components.Ids.IsEmpty ? 0 : components.Ids[^1] + 1];
        Array.Fill(_columnIndices, Absent);
        _itemsById = new object?[_columnIndices.Length];
        int column = 0;
        for (int i = 0; i < types.Length; i++)
        {
            int id = components.Ids[i];
            if (types[i].IsTag)
            {
                _columnIndices[id] = Tag;
            }
            else
            {
                _columns[column] = types[i].Create(InitialCapacity);
                _columnComponentIds[column] = id;
                _itemsById[id] = _columns[column].Items;
                _columnIndices[id] = column++;
            }
        }
    }

    public ComponentSet Components { get; }

    public int Count { get; private set; }

    // The component id of each column.
    public ReadOnlySpan<int> ColumnComponentIds => _columnComponentIds;

    public ReadOnlySpan<Entity> Entities => _entities.GetRows(0, Count);

    // The rows of T, which a query reads as one span. Throws for a tag, which has no data to read.
    public Span<T> GetSpan<T>() where T : struct
    {
        int column = GetColumnIndex(ComponentType<T>.Id);
        if (column < 0)
        {
            throw new InvalidOperationException(column == Tag
                ? $"{ComponentType<T>.Name} is a tag, which has no data to read."
                : $"The archetype has no {ComponentType<T>.Name} component.");
        }

        return GetColumnUnchecked<T>(column).GetRows(0, Count);
    }

    public Entity GetEntity(int row) => _entities.GetUnchecked(row);

    public Span<Entity> GetEntities(int row, int count) => _entities.GetRows(row, count);

    public int GetColumnIndex(int componentId) =>
        (uint)componentId < (uint)_columnIndices.Length ? Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_columnIndices), componentId) : -1;

    // The rows of T's column, or null when the archetype lacks T. The id must be T's, and T must not be a tag.
    public T[]? GetItems<T>(int componentId) where T : struct =>
        (uint)componentId < (uint)_itemsById.Length ? Unsafe.As<T[]?>(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_itemsById), componentId)) : null;

    // GetItems for an id the archetype has a column for.
    public T[] GetItemsUnchecked<T>(int componentId) where T : struct
    {
        Debug.Assert((uint)componentId < (uint)_itemsById.Length && _itemsById[componentId] is T[]);
        return Unsafe.As<T[]>(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_itemsById), componentId)!);
    }

    // The row of the items from GetItems, below the capacity.
    public static ref T GetRowUnchecked<T>(T[] items, int row) where T : struct
    {
        Debug.Assert((uint)row < (uint)items.Length);
        return ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(items), row);
    }

    public Column<T> GetColumn<T>(int index) where T : struct => (Column<T>)_columns[index];

    // The index must come from GetColumnIndex for T's id, so it is in range and the column holds T.
    public Column<T> GetColumnUnchecked<T>(int index) where T : struct
    {
        Debug.Assert(_columns[index] is Column<T>);
        return Unsafe.As<Column<T>>(Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(_columns), index));
    }

    public ColumnType[] GetColumnTypesWith(ColumnType added, ComponentSet target)
    {
        ColumnType[] types = new ColumnType[target.Ids.Length];
        for (int i = 0; i < types.Length; i++)
        {
            int position = Components.IndexOf(target.Ids[i]);
            types[i] = position < 0 ? added : _types[position];
        }

        return types;
    }

    public ColumnType[] GetColumnTypesWithout(ComponentSet target)
    {
        ColumnType[] types = new ColumnType[target.Ids.Length];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = _types[Components.IndexOf(target.Ids[i])];
        }

        return types;
    }

    public ArchetypeEdge? GetAddEdge(int componentId) => (uint)componentId < (uint)_addEdges.Length ? _addEdges[componentId] : null;

    public ArchetypeEdge? GetRemoveEdge(int componentId) => (uint)componentId < (uint)_removeEdges.Length ? _removeEdges[componentId] : null;

    public void SetAddEdge(int componentId, ArchetypeEdge edge) => SetEdge(ref _addEdges, componentId, edge);

    public void SetRemoveEdge(int componentId, ArchetypeEdge edge) => SetEdge(ref _removeEdges, componentId, edge);

    // Only for an archetype without components, whose rows are entities alone.
    public int AddRow(Entity entity)
    {
        Debug.Assert(_columns.Length == 0);
        int row = AddRow();
        _entities.GetUnchecked(row) = entity;
        return row;
    }

    // Adds count rows at the end, for the caller to fill.
    public int AddRows(int count)
    {
        int firstRow = Count;
        GrowFor(firstRow + count);

        Count = firstRow + count;
        return firstRow;
    }

    // Drops the last count rows, which AddRows added.
    public void RemoveLastRows(int count)
    {
        Count -= count;
        foreach (Column column in _columns)
        {
            column.ClearRows(Count, count);
        }
    }

    // Copies the row straight into a new row of the edge's target, with the components both archetypes have, and swap-removes it here.
    // A component only the target has is left for the caller to write.
    public int MoveRow(int row, ArchetypeEdge edge)
    {
        Archetype target = edge.Target;
        int targetRow = target.AddRow();
        int lastRow = Count - 1;
        _entities.MoveRow(row, lastRow, target._entities, targetRow);
        Column[] columns = _columns;
        int[] targetColumns = edge.TargetColumns;
        for (int i = 0; i < columns.Length; i++)
        {
            int targetColumn = targetColumns[i];
            if (targetColumn < 0)
            {
                columns[i].RemoveRow(row, lastRow);
            }
            else
            {
                columns[i].MoveRow(row, lastRow, target._columns[targetColumn], targetRow);
            }
        }

        Count = lastRow;
        return targetRow;
    }

    public void RemoveRow(int row)
    {
        int lastRow = Count - 1;
        _entities.RemoveRow(row, lastRow);
        foreach (Column column in _columns)
        {
            column.RemoveRow(row, lastRow);
        }

        Count = lastRow;
    }

    private int AddRow()
    {
        int row = Count;
        GrowFor(row + 1);

        Count = row + 1;
        return row;
    }

    // Grows the columns to exactly the capacity, for a known number of rows, so they grow once and leave no smaller arrays behind.
    public int EnsureCapacity(int capacity)
    {
        if (capacity > _capacity)
        {
            Grow(capacity);
        }

        return _capacity;
    }

    // Doubles the capacity, or grows it to the rows if that is more, so adding rows one by one copies each row a constant number of times.
    private void GrowFor(int rows)
    {
        if (rows > _capacity)
        {
            Grow(Math.Max(rows, _capacity * 2));
        }
    }

    // Copies the existing rows into new arrays of the capacity.
    private void Grow(int capacity)
    {
        _entities.Grow(capacity, Count);
        for (int i = 0; i < _columns.Length; i++)
        {
            _columns[i].Grow(capacity, Count);
            _itemsById[_columnComponentIds[i]] = _columns[i].Items;
        }

        _capacity = capacity;
    }

    private static void SetEdge(ref ArchetypeEdge?[] edges, int componentId, ArchetypeEdge edge)
    {
        if (componentId >= edges.Length)
        {
            Array.Resize(ref edges, Math.Max(componentId + 1, edges.Length * 2));
        }

        edges[componentId] = edge;
    }
}

// The archetype reached from a source by adding or removing one component type, with the target column of each source column.
internal sealed class ArchetypeEdge
{
    public ArchetypeEdge(Archetype source, Archetype target, int componentId)
    {
        Target = target;
        TargetColumns = new int[source.ColumnComponentIds.Length];
        for (int i = 0; i < TargetColumns.Length; i++)
        {
            TargetColumns[i] = target.GetColumnIndex(source.ColumnComponentIds[i]);
        }

        AddedColumn = target.GetColumnIndex(componentId);
    }

    public Archetype Target { get; }

    // The target column of each source column, or Absent for the removed component.
    public int[] TargetColumns { get; }

    // The target column of the added component, Tag for an added tag, or Absent on an edge that removes one.
    public int AddedColumn { get; }
}
