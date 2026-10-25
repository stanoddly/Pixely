namespace Pixely.Ecs;

// The Pixely.Ecs generator registers every Span<T> and ReadOnlySpan<T> that a Query<...> in an assembly names, when the assembly loads.
// The library cannot get T from a span type argument without reflection, so a query reads only the spans registered here.
public static class QueryTerms
{
    public static void RegisterSpan<T>() where T : struct =>
        QueryTerm<Span<T>>.Instance ??= new QueryTerm<Span<T>>(static description => description.Read<T>(), static archetype => archetype.GetSpan<T>());

    public static void RegisterReadOnlySpan<T>() where T : struct =>
        QueryTerm<ReadOnlySpan<T>>.Instance ??= new QueryTerm<ReadOnlySpan<T>>(static description => description.Read<T>(), static archetype => archetype.GetSpan<T>());
}

// How a query describes and creates the span of one type argument. ReadOnlySpan<Entity> needs no registration, so it has its term from the start.
internal sealed class QueryTerm<TSpan> where TSpan : allows ref struct
{
    public static QueryTerm<TSpan>? Instance = typeof(TSpan) == typeof(ReadOnlySpan<Entity>)
        ? (QueryTerm<TSpan>)(object)new QueryTerm<ReadOnlySpan<Entity>>(static _ => { }, static archetype => archetype.Entities)
        : null;

    public QueryTerm(Action<QueryDescription> describe, Func<Archetype, TSpan> create)
    {
        Describe = describe;
        Create = create;
    }

    public Action<QueryDescription> Describe { get; }

    public Func<Archetype, TSpan> Create { get; }
}
