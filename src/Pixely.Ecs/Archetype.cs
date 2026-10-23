using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Pixely.Ecs;

// The entities that have exactly the same component types, with one column per type in the order of the set.
// Rows are stored in chunks; a row's chunk is row >> ChunkShift.
internal sealed class Archetype
{
    private const int ChunkBytes = 16 * 1024;

    private readonly Column<Entity> _entities;
    private readonly Column[] _columns;

    // The column of each component id, or -1. Ids allocated after the archetype was created lie past its end.
    private readonly int[] _columnIndices;

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

    // Archetypes reached by adding or removing one component type, cached so structural changes skip the set lookup.
    public Dictionary<int, Archetype> AddEdges { get; } = new Dictionary<int, Archetype>();
    public Dictionary<int, Archetype> RemoveEdges { get; } = new Dictionary<int, Archetype>();

    public int Count => _entities.Count;

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

    // Only for an archetype without components, whose rows are entities alone.
    public int AddRow(Entity entity)
    {
        Debug.Assert(_columns.Length == 0);
        return _entities.Add(entity);
    }

    // Grows the entities and every column by count rows, for the caller to fill.
    public int AddRows(int count)
    {
        int firstRow = Count;
        _entities.Grow(count);
        foreach (Column column in _columns)
        {
            column.Grow(count);
        }

        return firstRow;
    }

    // Drops the last count rows, which AddRows added.
    public void RemoveLastRows(int count)
    {
        _entities.Shrink(count);
        foreach (Column column in _columns)
        {
            column.Shrink(count);
        }
    }

    // Appends the row to the target with the components both archetypes have and swap-removes it here.
    // A column the target lacks drops its value, and a column only the target has is left for the caller to append.
    public int MoveRow(int row, Archetype target)
    {
        int targetRow = target._entities.Add(GetEntity(row));
        for (int i = 0; i < _columns.Length; i++)
        {
            int targetIndex = target.GetColumnIndex(Components.Ids[i]);
            if (targetIndex < 0)
            {
                _columns[i].RemoveRow(row);
            }
            else
            {
                _columns[i].MoveRow(row, target._columns[targetIndex]);
            }
        }

        _entities.RemoveRow(row);
        return targetRow;
    }

    public void RemoveRow(int row)
    {
        foreach (Column column in _columns)
        {
            column.RemoveRow(row);
        }

        _entities.RemoveRow(row);
    }
}
