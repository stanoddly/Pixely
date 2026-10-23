using Pixely.Collections;

namespace Pixely.Ecs;

internal static class QueryIds
{
    private static int _count;

    internal static int Next() => Interlocked.Increment(ref _count) - 1;
}

internal static class QueryId<TQuery> where TQuery : allows ref struct
{
    internal static readonly int Value = QueryIds.Next();
}

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
public ref struct QueryEnumerator<TQuery> where TQuery : IQuery<TQuery>, allows ref struct
{
    private readonly World _world;
    private readonly FastList<Archetype> _archetypes;
    private int _index;
    private bool _disposed;

    internal QueryEnumerator(World world, FastList<Archetype> archetypes)
    {
        _world = world;
        _archetypes = archetypes;
        _index = -1;
        world.BeginQuery();
    }

    public TQuery Current => TQuery.Create(_archetypes[_index]);

    public bool MoveNext()
    {
        while (++_index < _archetypes.Length)
        {
            if (_archetypes[_index].Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _world.EndQuery();
        }
    }
}
