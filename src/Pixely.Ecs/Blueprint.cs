namespace Pixely.Ecs;

// The [Blueprint] source generator implements this; a hand-written blueprint implements it directly.
public interface IBlueprint<TSelf> where TSelf : struct, IBlueprint<TSelf>
{
    static abstract void Describe(BlueprintDescription description);

    static abstract void Write(ReadOnlySpan<TSelf> blueprints, BlueprintColumns columns);
}

[AttributeUsage(AttributeTargets.Struct)]
public sealed class BlueprintAttribute : Attribute;

public sealed class BlueprintDescription
{
    private readonly List<int> _ids = [];
    private readonly List<ColumnType> _types = [];

    internal BlueprintDescription()
    {
    }

    internal IReadOnlyList<int> Ids => _ids;

    internal IReadOnlyList<ColumnType> Types => _types;

    // Each call describes the next field, whose column Write then gets from BlueprintColumns by the same index.
    public void Add<T>() where T : struct
    {
        _ids.Add(ComponentType<T>.Id);
        _types.Add(ColumnType<T>.Instance);
    }
}

public readonly ref struct BlueprintColumns
{
    private readonly Archetype _archetype;
    private readonly ReadOnlySpan<int> _columns;
    private readonly int _firstRow;
    private readonly int _count;

    internal BlueprintColumns(Archetype archetype, ReadOnlySpan<int> columns, int firstRow, int count)
    {
        _archetype = archetype;
        _columns = columns;
        _firstRow = firstRow;
        _count = count;
    }

    // The rows of the spawned entities in the column of the field at this index, in the order of the blueprints. Throws for a tag, which has no column.
    public Span<T> Get<T>(int field) where T : struct
    {
        int column = _columns[field];
        if (column < 0)
        {
            throw new InvalidOperationException($"The field {field} is the tag {ComponentType<T>.Name}, which has no column to write.");
        }

        return _archetype.GetColumn<T>(column).GetRows(_firstRow, _count);
    }
}

internal sealed class BlueprintIds : StaticTypeIdMap<BlueprintIds>;

internal sealed class BlueprintId<TBlueprint> : StaticTypeIdMap<BlueprintIds, TBlueprint>;

// The archetype a blueprint spawns into, and the column of each of its fields.
internal sealed class BlueprintState
{
    public BlueprintState(Archetype archetype, int[] columns)
    {
        Archetype = archetype;
        Columns = columns;
    }

    public Archetype Archetype { get; }

    public int[] Columns { get; }
}
