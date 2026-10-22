using Pixely.Collections;

namespace Pixely.Ecs;

// The entities that have exactly the same component types, with one column per type in the order of the set.
public sealed class Archetype
{
    private readonly FastList<Entity> _entities = new FastList<Entity>();
    private readonly Column[] _columns;

    internal Archetype(ComponentSet components, Column[] columns)
    {
        Components = components;
        _columns = columns;
    }

    internal ComponentSet Components { get; }

    // Archetypes reached by adding or removing one component type, cached so structural changes skip the set lookup.
    internal Dictionary<int, Archetype> AddEdges { get; } = new Dictionary<int, Archetype>();
    internal Dictionary<int, Archetype> RemoveEdges { get; } = new Dictionary<int, Archetype>();

    public int Count => _entities.Length;

    public ReadOnlySpan<Entity> Entities => _entities.AsReadOnlySpan();

    public bool Has<T>() where T : struct => Components.Contains(ComponentType<T>.Id);

    public Span<T> GetSpan<T>() where T : struct
    {
        int index = Components.IndexOf(ComponentType<T>.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"The archetype has no {typeof(T).Name} component.");
        }

        return ((Column<T>)_columns[index]).Values.AsSpan();
    }

    internal Column<T> GetColumn<T>(int index) where T : struct => (Column<T>)_columns[index];

    internal Column[] CreateColumnsWith(Column added, ComponentSet target)
    {
        Column[] columns = new Column[target.Ids.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            int sourceIndex = Components.IndexOf(target.Ids[i]);
            columns[i] = sourceIndex < 0 ? added : _columns[sourceIndex].CreateEmpty();
        }

        return columns;
    }

    internal Column[] CreateColumnsWithout(ComponentSet target)
    {
        Column[] columns = new Column[target.Ids.Length];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = _columns[Components.IndexOf(target.Ids[i])].CreateEmpty();
        }

        return columns;
    }

    internal int AddRow(Entity entity) => _entities.Add(entity);

    // Appends the row to the target with the components both archetypes have and swap-removes it here.
    // A column the target lacks drops its value, and a column only the target has is left for the caller to append.
    internal int MoveRow(int row, Archetype target)
    {
        int targetRow = target._entities.Add(_entities[row]);
        for (int i = 0; i < _columns.Length; i++)
        {
            int targetIndex = target.Components.IndexOf(Components.Ids[i]);
            if (targetIndex < 0)
            {
                _columns[i].RemoveRow(row);
            }
            else
            {
                _columns[i].MoveRow(row, target._columns[targetIndex]);
            }
        }

        _entities.SwapRemove(row);
        return targetRow;
    }

    internal void RemoveRow(int row)
    {
        foreach (Column column in _columns)
        {
            column.RemoveRow(row);
        }

        _entities.SwapRemove(row);
    }
}
