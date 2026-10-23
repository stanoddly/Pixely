namespace Pixely.Ecs;

// The [Blueprint] source generator implements this; a hand-written blueprint implements it directly.
public interface IBlueprint<TSelf> where TSelf : struct, IBlueprint<TSelf>
{
    static abstract void Describe(BlueprintDescription description);

    static abstract void Write(in TSelf blueprint, BlueprintRow row);
}

[AttributeUsage(AttributeTargets.Struct)]
public sealed class BlueprintAttribute : Attribute;

public sealed class BlueprintDescription
{
    private readonly List<int> _ids = [];
    private readonly List<Column> _columns = [];

    internal BlueprintDescription()
    {
    }

    internal IReadOnlyList<int> Ids => _ids;

    internal IReadOnlyList<Column> Columns => _columns;

    // Each call describes the next field, which Write then passes to BlueprintRow.Set by the same index.
    public void Add<T>() where T : struct
    {
        _ids.Add(ComponentType<T>.Id);
        _columns.Add(new Column<T>());
    }
}

public readonly ref struct BlueprintRow
{
    private readonly Archetype _archetype;
    private readonly ReadOnlySpan<int> _columns;
    private readonly int _row;

    internal BlueprintRow(Archetype archetype, ReadOnlySpan<int> columns, int row)
    {
        _archetype = archetype;
        _columns = columns;
        _row = row;
    }

    public void Set<T>(int field, in T value) where T : struct => _archetype.GetColumn<T>(_columns[field]).Values[_row] = value;
}

internal static class BlueprintIds
{
    private static int _count;

    internal static int Next() => Interlocked.Increment(ref _count) - 1;
}

internal static class BlueprintId<TBlueprint>
{
    internal static readonly int Value = BlueprintIds.Next();
}

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
