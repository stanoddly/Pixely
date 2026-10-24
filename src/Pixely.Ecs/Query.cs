using Pixely.Collections;

namespace Pixely.Ecs;

internal sealed class QueryIds : StaticTypeIdMap<QueryIds>;

internal sealed class QueryId<TQuery> : StaticTypeIdMap<QueryIds, TQuery>;

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

// A query of the components its Span<T> and ReadOnlySpan<T> type arguments name, filtered by IWith<...> and IWithout<...>. A query derives from Query<...>;
// World.Query creates one instance per world and type, so a query class needs a public parameterless constructor.
public abstract class Query
{
    private World? _world;
    private QueryState? _state;

    private protected Query()
    {
    }

    internal QueryState State => _state!;

    internal void Initialize(World world)
    {
        QueryDescription description = new QueryDescription($"The query {GetType().Name}");
        DescribeTerms(description);
        if (this is IWith with)
        {
            with.DescribeWith(description);
        }

        if (this is IWithout without)
        {
            without.DescribeWithout(description);
        }

        _state = description.CreateState();
        _world = world;
    }

    private protected abstract void DescribeTerms(QueryDescription description);

    private protected void DescribeTerm<TSpan>(QueryDescription description) where TSpan : allows ref struct
    {
        if (QueryTerm<TSpan>.Instance is not { } term)
        {
            throw new InvalidOperationException(
                $"The type argument {FormatType(typeof(TSpan))} of the query {GetType().Name} is not registered. The Pixely.Ecs generator registers every Span<T> and ReadOnlySpan<T> of a component it sees in a Query<...>; reference it as an analyzer.");
        }

        term.Describe(description);
    }

    private protected ColumnsEnumerator<TColumns> Enumerate<TColumns>() where TColumns : IColumns<TColumns>, allows ref struct
    {
        if (_world is null)
        {
            throw new InvalidOperationException($"The query {GetType().Name} was created with new. Get it from World.Query or through a system's Update.");
        }

        return new ColumnsEnumerator<TColumns>(_world.QueryChunks(_state!));
    }

    private static string FormatType(Type type) =>
        type.IsGenericType ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>" : type.Name;
}

// The spans of one chunk, which Query<...> yields. The member is internal, so only the Columns<...> types implement it.
public interface IColumns<TSelf> where TSelf : IColumns<TSelf>, allows ref struct
{
    internal static abstract TSelf Create(ArchetypeChunk chunk);
}

// Holds the world's structural changes off from creation until Dispose, which foreach calls even on break or exception.
// Yields the columns of each chunk of each matching archetype.
public ref struct ColumnsEnumerator<TColumns> where TColumns : IColumns<TColumns>, allows ref struct
{
    private ChunkEnumerator _chunks;

    internal ColumnsEnumerator(ChunkEnumerator chunks)
    {
        _chunks = chunks;
    }

    public TColumns Current => TColumns.Create(_chunks.Current);

    public bool MoveNext() => _chunks.MoveNext();

    public void Dispose() => _chunks.Dispose();
}

// Holds the world's structural changes off from creation until Dispose, and yields each chunk of each matching archetype.
internal ref struct ChunkEnumerator
{
    private readonly World _world;
    private readonly FastList<Archetype> _archetypes;
    private readonly long _query;
    private int _archetypeIndex;
    private Archetype? _archetype;
    private int _chunk;
    private int _chunkCount;

    public ChunkEnumerator(World world, FastList<Archetype> archetypes)
    {
        _world = world;
        _archetypes = archetypes;
        _archetypeIndex = -1;
        _query = world.BeginQuery();
    }

    public ArchetypeChunk Current => new ArchetypeChunk(_archetype!, _chunk);

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
