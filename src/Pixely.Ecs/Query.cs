using Pixely.Collections;

namespace Pixely.Ecs;

internal sealed class QueryIds : StaticTypeIdMap<QueryIds>;

internal sealed class QueryId<TQuery> : StaticTypeIdMap<QueryIds, TQuery> where TQuery : allows ref struct;

internal sealed class QueryState
{
    private int _checkedArchetypes;

    public QueryState(ComponentSet required, ComponentSet excluded)
    {
        Required = required;
        Excluded = excluded;
    }

    public ComponentSet Required { get; }

    public ComponentSet Excluded { get; }

    public FastList<Archetype> Matches { get; } = new FastList<Archetype>(4);

    // Archetypes are never removed, so only the ones created since the last call need matching.
    public void Update(FastList<Archetype> archetypes)
    {
        for (; _checkedArchetypes < archetypes.Length; _checkedArchetypes++)
        {
            Archetype archetype = archetypes[_checkedArchetypes];
            if (archetype.Components.ContainsAll(Required) && !archetype.Components.ContainsAny(Excluded))
            {
                Matches.Add(archetype);
            }
        }
    }
}

// A query a system declares in its constructor. Enumerate it with foreach, as World.Query.
public sealed class Query<TQuery> where TQuery : IQuery<TQuery>, allows ref struct
{
    private readonly World _world;

    internal Query(World world)
    {
        _world = world;
    }

    public QueryEnumerator<TQuery> GetEnumerator() => _world.Query<TQuery>().GetEnumerator();
}

public readonly struct QueryEnumerable<TQuery> where TQuery : IQuery<TQuery>, allows ref struct
{
    private readonly World _world;
    private readonly QueryState _state;

    internal QueryEnumerable(World world, QueryState state)
    {
        _world = world;
        _state = state;
    }

    public QueryEnumerator<TQuery> GetEnumerator() => new QueryEnumerator<TQuery>(_world, _state.Matches);
}

// Holds the world's structural changes off from creation until Dispose, which foreach calls even on break or exception.
// Yields one query per chunk of each matching archetype.
public ref struct QueryEnumerator<TQuery> where TQuery : IQuery<TQuery>, allows ref struct
{
    private readonly World _world;
    private readonly FastList<Archetype> _archetypes;
    private readonly long _query;
    private int _archetypeIndex;
    private Archetype? _archetype;
    private int _chunk;
    private int _chunkCount;

    internal QueryEnumerator(World world, FastList<Archetype> archetypes)
    {
        _world = world;
        _archetypes = archetypes;
        _archetypeIndex = -1;
        _query = world.BeginQuery();
    }

    public TQuery Current => TQuery.Create(new ArchetypeChunk(_archetype!, _chunk));

    public bool MoveNext()
    {
        if (++_chunk < _chunkCount)
        {
            return true;
        }

        while (++_archetypeIndex < _archetypes.Length)
        {
            _archetype = _archetypes[_archetypeIndex];
            _chunkCount = _archetype.ChunkCount;
            if (_chunkCount > 0)
            {
                _chunk = 0;
                return true;
            }
        }

        return false;
    }

    public void Dispose() => _world.EndQuery(_query);
}
