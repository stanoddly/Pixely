using Pixely.Collections;

namespace Pixely.Ecs;

public sealed class World
{
    private struct EntityRecord
    {
        public int Version;
        public Archetype? Archetype;
        public int Row;
    }

    private readonly FastList<EntityRecord> _records = new FastList<EntityRecord>();
    private readonly FastList<int> _freeIndices = new FastList<int>();
    private readonly Dictionary<ComponentSet, Archetype> _archetypesBySet = new Dictionary<ComponentSet, Archetype>();
    private readonly FastList<Archetype> _archetypes = new FastList<Archetype>();
    private readonly FastList<QueryState?> _queries = new FastList<QueryState?>(4);
    private readonly Archetype _emptyArchetype;
    private int _activeQueries;

    public World()
    {
        _emptyArchetype = CreateArchetype(ComponentSet.Empty, []);
    }

    // Allowed while a query runs: a new entity is in the empty archetype, and no query matches it because each requires a component.
    public Entity Create()
    {
        int index;
        if (_freeIndices.Length > 0)
        {
            index = _freeIndices[_freeIndices.LastIndex];
            _freeIndices.RemoveLast();
        }
        else
        {
            index = _records.Add(new EntityRecord { Version = 1 });
        }

        ref EntityRecord record = ref _records[index];
        Entity entity = new Entity(index, record.Version);
        record.Archetype = _emptyArchetype;
        record.Row = _emptyArchetype.AddRow(entity);
        return entity;
    }

    public void Destroy(Entity entity)
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype archetype = record.Archetype!;
        int row = record.Row;

        archetype.RemoveRow(row);
        FixMovedRow(archetype, row);

        record.Archetype = null;
        record.Version = record.Version == int.MaxValue ? 1 : record.Version + 1;
        _freeIndices.Add(entity.Index);
    }

    public bool IsAlive(Entity entity)
    {
        return (uint)entity.Index < (uint)_records.Length && _records[entity.Index].Version == entity.Version && _records[entity.Index].Archetype is not null;
    }

    public bool Has<T>(Entity entity) where T : struct => GetRecord(entity).Archetype!.Has<T>();

    // The reference stays valid until the next structural change: Create, Destroy, Add or Remove.
    public ref T Get<T>(Entity entity) where T : struct
    {
        ref EntityRecord record = ref GetRecord(entity);
        int index = record.Archetype!.Components.IndexOf(ComponentType<T>.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"{entity} has no {typeof(T).Name} component.");
        }

        return ref record.Archetype.GetColumn<T>(index).Values[record.Row];
    }

    public void Add<T>(Entity entity, T component) where T : struct
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype source = record.Archetype!;
        int id = ComponentType<T>.Id;
        if (source.Components.Contains(id))
        {
            throw new InvalidOperationException($"{entity} already has a {typeof(T).Name} component.");
        }

        if (!source.AddEdges.TryGetValue(id, out Archetype? target))
        {
            ComponentSet components = source.Components.With(id);
            if (!_archetypesBySet.TryGetValue(components, out target))
            {
                target = CreateArchetype(components, source.CreateColumnsWith(new Column<T>(), components));
            }

            source.AddEdges[id] = target;
        }

        int row = record.Row;
        record.Row = source.MoveRow(row, target);
        record.Archetype = target;
        target.GetColumn<T>(target.Components.IndexOf(id)).Values.Add(component);
        FixMovedRow(source, row);
    }

    public void Remove<T>(Entity entity) where T : struct
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype source = record.Archetype!;
        int id = ComponentType<T>.Id;
        if (!source.Components.Contains(id))
        {
            throw new InvalidOperationException($"{entity} has no {typeof(T).Name} component.");
        }

        if (!source.RemoveEdges.TryGetValue(id, out Archetype? target))
        {
            ComponentSet components = source.Components.Without(id);
            if (!_archetypesBySet.TryGetValue(components, out target))
            {
                target = CreateArchetype(components, source.CreateColumnsWithout(components));
            }

            source.RemoveEdges[id] = target;
        }

        int row = record.Row;
        record.Row = source.MoveRow(row, target);
        record.Archetype = target;
        FixMovedRow(source, row);
    }

    public QueryEnumerable<TQuery> Query<TQuery>() where TQuery : IQuery<TQuery>, allows ref struct
    {
        int id = QueryId<TQuery>.Value;
        if (id >= _queries.Length)
        {
            _queries.Resize(id + 1);
        }

        ref QueryState? state = ref _queries[id];
        if (state is null)
        {
            QueryDescription description = new QueryDescription();
            TQuery.Describe(description);
            ComponentSet required = description.Required;
            if (required.Ids.IsEmpty)
            {
                throw new InvalidOperationException($"The query {typeof(TQuery).Name} must require at least one component.");
            }

            state = new QueryState(required, description.Excluded);
        }

        state.Update(_archetypes);
        return new QueryEnumerable<TQuery>(this, state);
    }

    internal void BeginQuery() => _activeQueries++;

    internal void EndQuery() => _activeQueries--;

    private void ThrowIfQueryRuns()
    {
        if (_activeQueries > 0)
        {
            throw new InvalidOperationException("Structural changes are not allowed while a query runs. Record them in a EntityCommandBuffer and apply it after the query.");
        }
    }

    private ref EntityRecord GetRecord(Entity entity)
    {
        if (!IsAlive(entity))
        {
            throw new ArgumentException($"{entity} is not alive.", nameof(entity));
        }

        return ref _records[entity.Index];
    }

    // A swap-remove moves the archetype's last entity into the removed row.
    private void FixMovedRow(Archetype archetype, int row)
    {
        if (row < archetype.Count)
        {
            _records[archetype.Entities[row].Index].Row = row;
        }
    }

    private Archetype CreateArchetype(ComponentSet components, Column[] columns)
    {
        Archetype archetype = new Archetype(components, columns);
        _archetypesBySet.Add(components, archetype);
        _archetypes.Add(archetype);
        return archetype;
    }
}
