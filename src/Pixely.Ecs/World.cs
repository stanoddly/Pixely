using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Pixely.Collections;

namespace Pixely.Ecs;

public sealed class World
{
    // What Get returns for a tag, which has no data to store.
    private static class TagValue<T> where T : struct
    {
        public static T Value;
    }

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
    private readonly FastList<Query?> _queries = new FastList<Query?>(4);
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

    public bool Has<T>(Entity entity) where T : struct => GetRecord(entity).Archetype!.GetColumnIndex(ComponentType<T>.Id) != Archetype.Absent;

    // The reference stays valid until the next structural change: Create, Destroy, Add or Remove. For a tag it is one value that all entities share.
    public ref T Get<T>(Entity entity) where T : struct
    {
        ref EntityRecord record = ref GetRecord(entity);
        Archetype archetype = record.Archetype!;
        // The JIT removes the branch that does not apply to T.
        if (default(T) is ITag)
        {
            if (archetype.GetColumnIndex(ComponentType<T>.Id) == Archetype.Absent)
            {
                ThrowMissing<T>(entity);
            }

            return ref TagValue<T>.Value;
        }

        T[]? items = archetype.GetItems<T>(ComponentType<T>.Id);
        if (items is null)
        {
            ThrowMissing<T>(entity);
        }

        return ref Archetype.GetRowUnchecked(items, record.Row);
    }

    // Get without its checks, for lookups in hot loops. The entity must be alive in this world and have T, which only Debug builds assert.
    // Otherwise the behavior is undefined: it may throw, return a reference into another entity's data, or read memory as the wrong type.
    public ref T UnsafeGet<T>(Entity entity) where T : struct
    {
        Debug.Assert(IsAlive(entity) && Has<T>(entity));
        if (default(T) is ITag)
        {
            return ref TagValue<T>.Value;
        }

        ref EntityRecord record = ref _records[entity.Index];
        Archetype archetype = record.Archetype!;
        return ref Archetype.GetRowUnchecked(archetype.GetItemsUnchecked<T>(ComponentType<T>.Id), record.Row);
    }

    public void Add<T>(Entity entity, T component) where T : struct
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype source = record.Archetype!;
        int id = ComponentType<T>.Id;
        if (source.GetColumnIndex(id) != Archetype.Absent)
        {
            ThrowPresent<T>(entity);
        }

        ArchetypeEdge? edge = source.GetAddEdge(id);
        if (edge is null)
        {
            ComponentSet components = source.Components.With(id);
            if (!_archetypesBySet.TryGetValue(components, out Archetype? target))
            {
                target = CreateArchetype(components, source.GetColumnTypesWith(ColumnType<T>.Instance, components));
            }

            edge = new ArchetypeEdge(source, target, id);
            source.SetAddEdge(id, edge);
        }

        int row = record.Row;
        record.Row = source.MoveRow(row, edge);
        record.Archetype = edge.Target;
        if (edge.AddedColumn >= 0)
        {
            edge.Target.GetColumnUnchecked<T>(edge.AddedColumn).GetUnchecked(record.Row) = component;
        }
        FixMovedRow(source, row);
    }

    public void Remove<T>(Entity entity) where T : struct
    {
        ThrowIfQueryRuns();
        ref EntityRecord record = ref GetRecord(entity);
        Archetype source = record.Archetype!;
        int id = ComponentType<T>.Id;
        if (source.GetColumnIndex(id) == Archetype.Absent)
        {
            ThrowMissing<T>(entity);
        }

        ArchetypeEdge? edge = source.GetRemoveEdge(id);
        if (edge is null)
        {
            ComponentSet components = source.Components.Without(id);
            if (!_archetypesBySet.TryGetValue(components, out Archetype? target))
            {
                target = CreateArchetype(components, source.GetColumnTypesWithout(components));
            }

            edge = new ArchetypeEdge(source, target, id);
            source.SetRemoveEdge(id, edge);
        }

        int row = record.Row;
        record.Row = source.MoveRow(row, edge);
        record.Archetype = edge.Target;
        FixMovedRow(source, row);
    }

    // Grows the archetype of the blueprint's components to hold capacity entities, so a known number of spawns or adds grows its columns once.
    // Returns the archetype's capacity. Throws while a query runs, because growing replaces the arrays its spans point into.
    public int EnsureCapacity<TBlueprint>(int capacity) where TBlueprint : struct, IBlueprint<TBlueprint>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ThrowIfQueryRuns();
        return GetBlueprintState<TBlueprint>().Archetype.EnsureCapacity(capacity);
    }

    // One instance per world and query type, created on first use. Enumerate it with foreach.
    public TQuery Query<TQuery>() where TQuery : Query, new()
    {
        int id = QueryId<TQuery>.Id;
        if (id >= _queries.Length)
        {
            _queries.Resize(id + 1);
        }

        ref Query? query = ref _queries[id];
        if (query is null)
        {
            TQuery created = new TQuery();
            created.Initialize(this);
            query = created;
        }

        return (TQuery)query;
    }

    internal ArchetypeEnumerator QueryArchetypes(QueryState state)
    {
        state.Update(_archetypes);
        return new ArchetypeEnumerator(this, state.Matches);
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
                // The spawner's two lists split into runs at different indices, so the shorter run sets the length.
                int length = blueprintRun.Length;
                entityRun[..length].CopyTo(archetype.GetEntities(row, length));
                TBlueprint.Write(blueprintRun, new BlueprintColumns(archetype, state.Columns, row, length));
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

    // Only a failed sync point releases, and only entities its spawner reserved, so the entity is reserved without a check.
    internal void ReleaseReservation(Entity entity)
    {
        ref EntityRecord record = ref _records[entity.Index];
        Debug.Assert(record.Version == entity.Version && record.Row == ReservedRow);
        FreeIndex(ref record, entity.Index);
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
                fieldColumns[i] = archetype.GetColumnIndex(description.Ids[i]);
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
        if ((uint)entity.Index >= (uint)_records.Count)
        {
            ThrowNotAlive(entity);
        }

        ref EntityRecord record = ref _records[entity.Index];
        if (record.Version != entity.Version || record.Archetype is null)
        {
            ThrowNotAlive(entity);
        }

        return ref record;
    }

    // The throws live in their own methods, which the JIT never inlines, so a caller that inlines Get does not set up the messages.
    [DoesNotReturn]
    private static void ThrowNotAlive(Entity entity) => throw new ArgumentException($"{entity} is not alive.", nameof(entity));

    [DoesNotReturn]
    private static void ThrowMissing<T>(Entity entity) where T : struct => throw new InvalidOperationException($"{entity} has no {ComponentType<T>.Name} component.");

    [DoesNotReturn]
    private static void ThrowPresent<T>(Entity entity) where T : struct => throw new InvalidOperationException($"{entity} already has a {ComponentType<T>.Name} component.");

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

    // A new version keeps the handles of the freed entity dead after the index is reused. An index whose version runs out is never reused, because
    // wrapping the version would revive the oldest handles; its record keeps the last version without an archetype, so those handles stay dead.
    private void FreeIndex(ref EntityRecord record, int index)
    {
        record.Row = 0;
        if (record.Version == int.MaxValue)
        {
            return;
        }

        record.Version++;
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
