using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Pixely.Collections;

namespace Pixely.Ecs;

// The entities and their components. Its members are internal: systems change it through their writers, spawners and lookups, and code outside
// systems reads it through the queries and lookups that AddQuery and AddReadOnlyComponentLookup register.
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
    private readonly FastList<SpawnerLayout?> _spawnerLayouts = new FastList<SpawnerLayout?>(4);

    // Each running query has an id, so ending a query twice, as copies of its enumerator do, ends it once.
    private readonly HashSet<long> _activeQueries = [];
    private long _lastQuery;

    internal void Destroy(Entity entity)
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

    internal bool IsAlive(Entity entity)
    {
        if ((uint)entity.Index >= (uint)_records.Count)
        {
            return false;
        }

        ref EntityRecord record = ref _records[entity.Index];
        return record.Version == entity.Version && record.Archetype is not null;
    }

    internal bool Has<T>(Entity entity) where T : struct => GetRecord(entity).Archetype!.GetColumnIndex(ComponentType<T>.Id) != Archetype.Absent;

    // Has for an entity that may not be alive, which has no components, for lookups that follow an entity another system may have destroyed.
    internal bool IsAliveWith<T>(Entity entity) where T : struct => FindArchetype(entity)?.GetColumnIndex(ComponentType<T>.Id) is not (null or Archetype.Absent);

    // The reference stays valid until the next structural change: Destroy, Add, Remove or a sync point. For a tag it is one value that all entities share.
    internal ref T Get<T>(Entity entity) where T : struct
    {
        ref EntityRecord record = ref GetRecord(entity);
        return ref GetComponent<T>(record.Archetype!, record.Row, entity);
    }

    // Get without its checks, for lookups in hot loops. The entity must be alive in this world and have T, which only Debug builds assert.
    // Otherwise the behavior is undefined: it may throw, return a reference into another entity's data, or read memory as the wrong type.
    internal ref T UnsafeGet<T>(Entity entity) where T : struct
    {
        Debug.Assert(IsAlive(entity) && Has<T>(entity));
        ref EntityRecord record = ref _records[entity.Index];
        return ref UnsafeGetComponent<T>(record.Archetype!, record.Row);
    }

    // The archetype of an entity that may not be alive, or null, for lookups that check several components of one entity.
    internal Archetype? FindArchetype(Entity entity) => IsAlive(entity) ? _records[entity.Index].Archetype : null;

    // The row of an entity and its archetype, for lookups that get several components of one entity. Throws for an entity that is not alive.
    internal int GetRow(Entity entity, out Archetype archetype)
    {
        ref EntityRecord record = ref GetRecord(entity);
        archetype = record.Archetype!;
        return record.Row;
    }

    // GetRow without its checks, with the undefined behavior of UnsafeGet.
    internal int UnsafeGetRow(Entity entity, out Archetype archetype)
    {
        Debug.Assert(IsAlive(entity));
        ref EntityRecord record = ref _records[entity.Index];
        archetype = record.Archetype!;
        return record.Row;
    }

    // T in the row of the archetype, which belongs to entity, the entity named when T is missing.
    internal static ref T GetComponent<T>(Archetype archetype, int row, Entity entity) where T : struct
    {
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

        return ref Archetype.GetRowUnchecked(items, row);
    }

    // GetComponent for an archetype that has T, with the undefined behavior of UnsafeGet otherwise.
    internal static ref T UnsafeGetComponent<T>(Archetype archetype, int row) where T : struct
    {
        if (default(T) is ITag)
        {
            return ref TagValue<T>.Value;
        }

        return ref Archetype.GetRowUnchecked(archetype.GetItemsUnchecked<T>(ComponentType<T>.Id), row);
    }

    internal void Add<T>(Entity entity, T component) where T : struct
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

    internal void Remove<T>(Entity entity) where T : struct
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

    // One instance per world and query type, created on first use. Enumerate it with foreach.
    internal TQuery Query<TQuery>() where TQuery : Query, new()
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

    // Grows the archetype to hold count entities more than it holds, for the room that spawners ask for at a sync point.
    internal void EnsureRoom(Archetype archetype, int count)
    {
        ThrowIfQueryRuns();
        archetype.EnsureCapacity(checked(archetype.Count + count));
    }

    // Allowed while a query runs, because the entity gets no archetype until Spawn.
    internal Entity Reserve()
    {
        int index = TakeIndex();
        ref EntityRecord record = ref _records[index];
        record.Row = ReservedRow;
        return new Entity(index, record.Version);
    }

    // Adds the rows of a run of spawns together, so each column grows once, for the spawner to copy its entities and components into.
    // Copying calls no code of the components, so nothing throws between this and SetSpawnedRecords.
    internal int AddSpawnedRows(Archetype archetype, int count)
    {
        ThrowIfQueryRuns();
        return archetype.AddRows(count);
    }

    // Only a Spawner of this world reserves, and only a failed sync point releases, so the entities are reserved without a check.
    internal void SetSpawnedRecords(Archetype archetype, int firstRow, int count)
    {
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

    // One layout per world and spawner type. describer saves creating a spawner only to describe it, when the caller has one.
    internal SpawnerLayout GetSpawnerLayout<TSpawner>(TSpawner? describer = null) where TSpawner : Spawner, new()
    {
        int id = SpawnerId<TSpawner>.Id;
        if (id < _spawnerLayouts.Length && _spawnerLayouts[id] is { } layout)
        {
            return layout;
        }

        return CreateSpawnerLayout(id, describer ?? new TSpawner());
    }

    // Runs once per world and spawner type. Kept out of line, because inlined into a caller that then adds components in a loop, it used up the
    // caller's inlining budget, so the JIT stopped inlining Add and the loop ran about 20% slower.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private SpawnerLayout CreateSpawnerLayout(int id, Spawner describer)
    {
        if (id >= _spawnerLayouts.Length)
        {
            _spawnerLayouts.Resize(id + 1);
        }

        SpawnerDescription description = describer.Describe();
        ComponentSet components = ComponentSet.From(description.Ids);
        if (!_archetypesBySet.TryGetValue(components, out Archetype? archetype))
        {
            ColumnType[] types = new ColumnType[description.Ids.Count];
            for (int i = 0; i < types.Length; i++)
            {
                types[components.IndexOf(description.Ids[i])] = description.Types[i];
            }

            archetype = CreateArchetype(components, types);
        }

        int[] columns = new int[description.ArgumentCount];
        for (int i = 0; i < columns.Length; i++)
        {
            columns[i] = archetype.GetColumnIndex(description.Ids[i]);
        }

        SpawnerLayout layout = new SpawnerLayout(archetype, columns);
        _spawnerLayouts[id] = layout;
        return layout;
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
