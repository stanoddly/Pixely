using System.Diagnostics;
using Pixely.Collections;

namespace Pixely.Ecs;

public sealed class World
{
    private struct EntityRecord
    {
        public int Version;
        public Archetype? Archetype;

        // ReservedRow, without an archetype, marks an entity a Spawner took and the sync point has not spawned yet.
        public int Row;
    }

    private const int ReservedRow = -1;

    private readonly ChunkedList<EntityRecord> _records = new ChunkedList<EntityRecord>();
    private readonly FastList<int> _freeIndices = new FastList<int>();
    private readonly Dictionary<ComponentSet, Archetype> _archetypesBySet = new Dictionary<ComponentSet, Archetype>();
    private readonly FastList<Archetype> _archetypes = new FastList<Archetype>();
    private readonly FastList<QueryState?> _queries = new FastList<QueryState?>(4);
    private readonly FastList<BlueprintState?> _blueprints = new FastList<BlueprintState?>(4);
    private readonly Archetype _emptyArchetype;

    // Each running query has an id, so ending a query twice, as copies of its enumerator do, ends it once.
    private readonly HashSet<long> _activeQueries = [];
    private long _lastQuery;

    public World()
    {
        _emptyArchetype = CreateArchetype(ComponentSet.Empty, []);
    }

    // Allowed while a query runs: a new entity is in the empty archetype, and no query matches it because each requires a component.
    public Entity Create()
    {
        int index = TakeIndex();
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
        FreeIndex(ref record, entity.Index);
    }

    public bool IsAlive(Entity entity)
    {
        if ((uint)entity.Index >= (uint)_records.Count)
        {
            return false;
        }

        ref EntityRecord record = ref _records[entity.Index];
        return record.Version == entity.Version && record.Archetype is not null;
    }

    public bool Has<T>(Entity entity) where T : struct => GetRecord(entity).Archetype!.GetColumnIndex(ComponentType<T>.Id) >= 0;

    // The reference stays valid until the next structural change: Create, Destroy, Add or Remove.
    public ref T Get<T>(Entity entity) where T : struct
    {
        ref EntityRecord record = ref GetRecord(entity);
        Archetype archetype = record.Archetype!;
        int index = archetype.GetColumnIndex(ComponentType<T>.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"{entity} has no {ComponentType<T>.Name} component.");
        }

        return ref archetype.GetColumnUnchecked<T>(index).GetUnchecked(record.Row);
    }

    public void Add<T>(Entity entity, T component) where T : struct
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype source = record.Archetype!;
        int id = ComponentType<T>.Id;
        if (source.GetColumnIndex(id) >= 0)
        {
            throw new InvalidOperationException($"{entity} already has a {ComponentType<T>.Name} component.");
        }

        if (!source.AddEdges.TryGetValue(id, out Archetype? target))
        {
            ComponentSet components = source.Components.With(id);
            if (!_archetypesBySet.TryGetValue(components, out target))
            {
                target = CreateArchetype(components, source.GetColumnTypesWith(ColumnType<T>.Instance, components));
            }

            source.AddEdges[id] = target;
        }

        int row = record.Row;
        record.Row = source.MoveRow(row, target);
        record.Archetype = target;
        target.GetColumnUnchecked<T>(target.GetColumnIndex(id)).Add(component);
        FixMovedRow(source, row);
    }

    public void Remove<T>(Entity entity) where T : struct
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype source = record.Archetype!;
        int id = ComponentType<T>.Id;
        if (source.GetColumnIndex(id) < 0)
        {
            throw new InvalidOperationException($"{entity} has no {ComponentType<T>.Name} component.");
        }

        if (!source.RemoveEdges.TryGetValue(id, out Archetype? target))
        {
            ComponentSet components = source.Components.Without(id);
            if (!_archetypesBySet.TryGetValue(components, out target))
            {
                target = CreateArchetype(components, source.GetColumnTypesWithout(components));
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
        QueryState state = GetQueryState<TQuery>();
        state.Update(_archetypes);
        return new QueryEnumerable<TQuery>(this, state);
    }

    internal QueryState GetQueryState<TQuery>() where TQuery : IQuery<TQuery>, allows ref struct
    {
        int id = QueryId<TQuery>.Id;
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

        return state;
    }

    // Allowed while a query runs, because the entity gets no archetype until Spawn.
    internal Entity Reserve()
    {
        int index = TakeIndex();
        ref EntityRecord record = ref _records[index];
        record.Row = ReservedRow;
        return new Entity(index, record.Version);
    }

    // Spawns a run of reserved entities together, so each column grows once and the blueprints are copied into it directly.
    // Only a Spawner of this world reserves, and only a failed sync point releases, so the entities are reserved without a check.
    // When a blueprint's Write throws, the rows are dropped and the entities stay reserved, for the failed sync point to release.
    internal void Spawn<TBlueprint>(ChunkedList<Entity> entities, ChunkedList<TBlueprint> blueprints, int first, int count)
        where TBlueprint : struct, IBlueprint<TBlueprint>
    {
        ThrowIfQueryRuns();
        BlueprintState state = GetBlueprintState<TBlueprint>();
        Archetype archetype = state.Archetype;
        int firstRow = archetype.AddRows(count);
        try
        {
            int row = firstRow;
            for (int index = first; index < first + count;)
            {
                ReadOnlySpan<Entity> entityRun = entities.GetRun(index, first + count - index);
                ReadOnlySpan<TBlueprint> blueprintRun = blueprints.GetRun(index, entityRun.Length);
                int length = Math.Min(blueprintRun.Length, archetype.GetRowsLeftInChunk(row));
                entityRun[..length].CopyTo(archetype.GetEntities(row, length));
                TBlueprint.Write(blueprintRun[..length], new BlueprintColumns(archetype, state.Columns, row, length));
                index += length;
                row += length;
            }
        }
        catch
        {
            archetype.RemoveLastRows(count);
            throw;
        }

        for (int row = firstRow; row < firstRow + count; row++)
        {
            Entity entity = archetype.GetEntity(row);
            ref EntityRecord record = ref _records[entity.Index];
            Debug.Assert(record.Version == entity.Version && record.Row == ReservedRow);
            record.Archetype = archetype;
            record.Row = row;
        }
    }

    internal void ReleaseReservation(Entity entity)
    {
        FreeIndex(ref GetReservedRecord(entity), entity.Index);
    }

    internal BlueprintState GetBlueprintState<TBlueprint>() where TBlueprint : struct, IBlueprint<TBlueprint>
    {
        int id = BlueprintId<TBlueprint>.Id;
        if (id >= _blueprints.Length)
        {
            _blueprints.Resize(id + 1);
        }

        ref BlueprintState? state = ref _blueprints[id];
        if (state is null)
        {
            BlueprintDescription description = new BlueprintDescription();
            TBlueprint.Describe(description);
            ComponentSet components = ComponentSet.From(description.Ids);
            if (components.Ids.Length != description.Ids.Count)
            {
                throw new InvalidOperationException($"The blueprint {typeof(TBlueprint).Name} describes a component more than once.");
            }

            if (!_archetypesBySet.TryGetValue(components, out Archetype? archetype))
            {
                ColumnType[] types = new ColumnType[description.Ids.Count];
                for (int i = 0; i < types.Length; i++)
                {
                    types[components.IndexOf(description.Ids[i])] = description.Types[i];
                }

                archetype = CreateArchetype(components, types);
            }

            int[] fieldColumns = new int[description.Ids.Count];
            for (int i = 0; i < fieldColumns.Length; i++)
            {
                fieldColumns[i] = components.IndexOf(description.Ids[i]);
            }

            state = new BlueprintState(archetype, fieldColumns);
        }

        return state;
    }

    internal long BeginQuery()
    {
        _activeQueries.Add(++_lastQuery);
        return _lastQuery;
    }

    internal void EndQuery(long query) => _activeQueries.Remove(query);

    private void ThrowIfQueryRuns()
    {
        if (_activeQueries.Count > 0)
        {
            throw new InvalidOperationException("Structural changes are not allowed while a query runs. A system makes them through its writers, which apply them at a sync point.");
        }
    }

    private ref EntityRecord GetRecord(Entity entity)
    {
        if ((uint)entity.Index < (uint)_records.Count)
        {
            ref EntityRecord record = ref _records[entity.Index];
            if (record.Version == entity.Version && record.Archetype is not null)
            {
                return ref record;
            }
        }

        throw new ArgumentException($"{entity} is not alive.", nameof(entity));
    }

    private ref EntityRecord GetReservedRecord(Entity entity)
    {
        if ((uint)entity.Index < (uint)_records.Count)
        {
            ref EntityRecord record = ref _records[entity.Index];
            if (record.Version == entity.Version && record.Row == ReservedRow)
            {
                return ref record;
            }
        }

        throw new ArgumentException($"{entity} is not reserved.", nameof(entity));
    }

    private int TakeIndex()
    {
        if (_freeIndices.Length == 0)
        {
            return _records.Add(new EntityRecord { Version = 1 });
        }

        int index = _freeIndices[_freeIndices.LastIndex];
        _freeIndices.RemoveLast();
        return index;
    }

    // A new version keeps the handles of the freed entity dead after the index is reused.
    private void FreeIndex(ref EntityRecord record, int index)
    {
        record.Row = 0;
        record.Version = record.Version == int.MaxValue ? 1 : record.Version + 1;
        _freeIndices.Add(index);
    }

    // A swap-remove moves the archetype's last entity into the removed row.
    private void FixMovedRow(Archetype archetype, int row)
    {
        if (row < archetype.Count)
        {
            _records[archetype.GetEntity(row).Index].Row = row;
        }
    }

    private Archetype CreateArchetype(ComponentSet components, ColumnType[] types)
    {
        Archetype archetype = new Archetype(components, types);
        _archetypesBySet.Add(components, archetype);
        _archetypes.Add(archetype);
        return archetype;
    }
}
