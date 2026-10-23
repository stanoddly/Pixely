using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

// The entities that have exactly the same component types, with one column per type in the order of the set.
// Rows are stored in chunks; a row's chunk is row >> ChunkShift. The archetype counts the rows for all its columns and adds their chunks together.
internal sealed class Archetype
{
    private const int ChunkBytes = 16 * 1024;

    private readonly Column<Entity> _entities;
    private readonly Column[] _columns;

    // The column of each component id, or -1. Ids allocated after the archetype was created lie past its end.
    private readonly int[] _columnIndices;

    // The edges reached by adding or removing each component id, or null until first used.
    private ArchetypeEdge?[] _addEdges = [];
    private ArchetypeEdge?[] _removeEdges = [];

    // The rows the added chunks hold.
    private int _capacity;

    public Archetype(ComponentSet components, ColumnType[] types)
    {
        Components = components;

        // The largest power of two of rows that fits ChunkBytes, at least 64, so a full chunk of any component is a whole number of vectors up to 512 bits.
        int rowSize = Unsafe.SizeOf<Entity>();
        foreach (ColumnType type in types)
        {
            rowSize += type.Size;
        }

        ChunkShift = BitOperations.Log2((uint)Math.Max(64, ChunkBytes / rowSize));
        _entities = new Column<Entity>(ChunkShift);
        _columns = new Column[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            _columns[i] = types[i].Create(ChunkShift);
        }

        _columnIndices = new int[components.Ids.IsEmpty ? 0 : components.Ids[^1] + 1];
        Array.Fill(_columnIndices, -1);
        for (int i = 0; i < components.Ids.Length; i++)
        {
            _columnIndices[components.Ids[i]] = i;
        }
    }

    public ComponentSet Components { get; }

    public int ChunkShift { get; }

    public int Count { get; private set; }

    public int ChunkCount => (Count + ChunkLength - 1) >> ChunkShift;

    private int ChunkLength => 1 << ChunkShift;

    public int GetChunkCount(int chunk) => Math.Min(ChunkLength, Count - (chunk << ChunkShift));

    public ReadOnlySpan<Entity> GetChunkEntities(int chunk) => _entities.GetChunk(chunk, GetChunkCount(chunk));

    public int GetRowsLeftInChunk(int row) => ChunkLength - (row & (ChunkLength - 1));

    public Entity GetEntity(int row) => _entities.GetUnchecked(row);

    public Span<Entity> GetEntities(int row, int count) => _entities.GetRows(row, count);

    public int GetColumnIndex(int componentId) => (uint)componentId < (uint)_columnIndices.Length ? _columnIndices[componentId] : -1;

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
            int sourceIndex = GetColumnIndex(target.Ids[i]);
            types[i] = sourceIndex < 0 ? added : _columns[sourceIndex].Type;
        }

        return types;
    }

    public ColumnType[] GetColumnTypesWithout(ComponentSet target)
    {
        ColumnType[] types = new ColumnType[target.Ids.Length];
        for (int i = 0; i < types.Length; i++)
        {
            types[i] = _columns[GetColumnIndex(target.Ids[i])].Type;
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
        while (_capacity < firstRow + count)
        {
            AddChunk();
        }

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
        if (row == _capacity)
        {
            AddChunk();
        }

        Count = row + 1;
        return row;
    }

    private void AddChunk()
    {
        _entities.AddChunk();
        foreach (Column column in _columns)
        {
            column.AddChunk();
        }

        _capacity += ChunkLength;
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
        TargetColumns = new int[source.Components.Ids.Length];
        for (int i = 0; i < TargetColumns.Length; i++)
        {
            TargetColumns[i] = target.GetColumnIndex(source.Components.Ids[i]);
        }

        AddedColumn = target.GetColumnIndex(componentId);
    }

    public Archetype Target { get; }

    // The target column of each source column, or -1 for the removed component.
    public int[] TargetColumns { get; }

    // The target column of the added component, or -1 on an edge that removes one.
    public int AddedColumn { get; }
}
