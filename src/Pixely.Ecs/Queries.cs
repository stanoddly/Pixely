namespace Pixely.Ecs;

// A query whose type arguments are Span<T>, which it may write, ReadOnlySpan<T>, which it only reads, or ReadOnlySpan<Entity>.
// Derive a class from it to name the query and filter it with IWith<...> and IWithout<...>.
public class Query<T1> : Query
    where T1 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1>> GetEnumerator() => Enumerate<Columns<T1>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
    }
}

public class Query<T1, T2> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2>> GetEnumerator() => Enumerate<Columns<T1, T2>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
    }
}

public class Query<T1, T2, T3> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2, T3>> GetEnumerator() => Enumerate<Columns<T1, T2, T3>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
        DescribeTerm<T3>(description);
    }
}

public class Query<T1, T2, T3, T4> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2, T3, T4>> GetEnumerator() => Enumerate<Columns<T1, T2, T3, T4>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
        DescribeTerm<T3>(description);
        DescribeTerm<T4>(description);
    }
}

public class Query<T1, T2, T3, T4, T5> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2, T3, T4, T5>> GetEnumerator() => Enumerate<Columns<T1, T2, T3, T4, T5>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
        DescribeTerm<T3>(description);
        DescribeTerm<T4>(description);
        DescribeTerm<T5>(description);
    }
}

public class Query<T1, T2, T3, T4, T5, T6> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2, T3, T4, T5, T6>> GetEnumerator() => Enumerate<Columns<T1, T2, T3, T4, T5, T6>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
        DescribeTerm<T3>(description);
        DescribeTerm<T4>(description);
        DescribeTerm<T5>(description);
        DescribeTerm<T6>(description);
    }
}

public class Query<T1, T2, T3, T4, T5, T6, T7> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
    where T7 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2, T3, T4, T5, T6, T7>> GetEnumerator() => Enumerate<Columns<T1, T2, T3, T4, T5, T6, T7>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
        DescribeTerm<T3>(description);
        DescribeTerm<T4>(description);
        DescribeTerm<T5>(description);
        DescribeTerm<T6>(description);
        DescribeTerm<T7>(description);
    }
}

public class Query<T1, T2, T3, T4, T5, T6, T7, T8> : Query
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
    where T7 : allows ref struct
    where T8 : allows ref struct
{
    public ColumnsEnumerator<Columns<T1, T2, T3, T4, T5, T6, T7, T8>> GetEnumerator() => Enumerate<Columns<T1, T2, T3, T4, T5, T6, T7, T8>>();

    private protected override void DescribeTerms(QueryDescription description)
    {
        DescribeTerm<T1>(description);
        DescribeTerm<T2>(description);
        DescribeTerm<T3>(description);
        DescribeTerm<T4>(description);
        DescribeTerm<T5>(description);
        DescribeTerm<T6>(description);
        DescribeTerm<T7>(description);
        DescribeTerm<T8>(description);
    }
}

// The spans of one matching archetype, in the order of the query's type arguments. foreach deconstructs them into named variables.
// The query checked that each type argument is registered when it was created.
public readonly ref struct Columns<T1> : IColumns<Columns<T1>>
    where T1 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    static Columns<T1> IColumns<Columns<T1>>.Create(Archetype archetype) => new Columns<T1>(archetype);
}

public readonly ref struct Columns<T1, T2> : IColumns<Columns<T1, T2>>
    where T1 : allows ref struct
    where T2 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2> IColumns<Columns<T1, T2>>.Create(Archetype archetype) => new Columns<T1, T2>(archetype);
}

public readonly ref struct Columns<T1, T2, T3> : IColumns<Columns<T1, T2, T3>>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public T3 Item3 => QueryTerm<T3>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
        item3 = QueryTerm<T3>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2, T3> IColumns<Columns<T1, T2, T3>>.Create(Archetype archetype) => new Columns<T1, T2, T3>(archetype);
}

public readonly ref struct Columns<T1, T2, T3, T4> : IColumns<Columns<T1, T2, T3, T4>>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public T3 Item3 => QueryTerm<T3>.Instance!.Create(_archetype);

    public T4 Item4 => QueryTerm<T4>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3, out T4 item4)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
        item3 = QueryTerm<T3>.Instance!.Create(_archetype);
        item4 = QueryTerm<T4>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2, T3, T4> IColumns<Columns<T1, T2, T3, T4>>.Create(Archetype archetype) => new Columns<T1, T2, T3, T4>(archetype);
}

public readonly ref struct Columns<T1, T2, T3, T4, T5> : IColumns<Columns<T1, T2, T3, T4, T5>>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public T3 Item3 => QueryTerm<T3>.Instance!.Create(_archetype);

    public T4 Item4 => QueryTerm<T4>.Instance!.Create(_archetype);

    public T5 Item5 => QueryTerm<T5>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
        item3 = QueryTerm<T3>.Instance!.Create(_archetype);
        item4 = QueryTerm<T4>.Instance!.Create(_archetype);
        item5 = QueryTerm<T5>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2, T3, T4, T5> IColumns<Columns<T1, T2, T3, T4, T5>>.Create(Archetype archetype) => new Columns<T1, T2, T3, T4, T5>(archetype);
}

public readonly ref struct Columns<T1, T2, T3, T4, T5, T6> : IColumns<Columns<T1, T2, T3, T4, T5, T6>>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public T3 Item3 => QueryTerm<T3>.Instance!.Create(_archetype);

    public T4 Item4 => QueryTerm<T4>.Instance!.Create(_archetype);

    public T5 Item5 => QueryTerm<T5>.Instance!.Create(_archetype);

    public T6 Item6 => QueryTerm<T6>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
        item3 = QueryTerm<T3>.Instance!.Create(_archetype);
        item4 = QueryTerm<T4>.Instance!.Create(_archetype);
        item5 = QueryTerm<T5>.Instance!.Create(_archetype);
        item6 = QueryTerm<T6>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2, T3, T4, T5, T6> IColumns<Columns<T1, T2, T3, T4, T5, T6>>.Create(Archetype archetype) => new Columns<T1, T2, T3, T4, T5, T6>(archetype);
}

public readonly ref struct Columns<T1, T2, T3, T4, T5, T6, T7> : IColumns<Columns<T1, T2, T3, T4, T5, T6, T7>>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
    where T7 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public T3 Item3 => QueryTerm<T3>.Instance!.Create(_archetype);

    public T4 Item4 => QueryTerm<T4>.Instance!.Create(_archetype);

    public T5 Item5 => QueryTerm<T5>.Instance!.Create(_archetype);

    public T6 Item6 => QueryTerm<T6>.Instance!.Create(_archetype);

    public T7 Item7 => QueryTerm<T7>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6, out T7 item7)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
        item3 = QueryTerm<T3>.Instance!.Create(_archetype);
        item4 = QueryTerm<T4>.Instance!.Create(_archetype);
        item5 = QueryTerm<T5>.Instance!.Create(_archetype);
        item6 = QueryTerm<T6>.Instance!.Create(_archetype);
        item7 = QueryTerm<T7>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2, T3, T4, T5, T6, T7> IColumns<Columns<T1, T2, T3, T4, T5, T6, T7>>.Create(Archetype archetype) => new Columns<T1, T2, T3, T4, T5, T6, T7>(archetype);
}

public readonly ref struct Columns<T1, T2, T3, T4, T5, T6, T7, T8> : IColumns<Columns<T1, T2, T3, T4, T5, T6, T7, T8>>
    where T1 : allows ref struct
    where T2 : allows ref struct
    where T3 : allows ref struct
    where T4 : allows ref struct
    where T5 : allows ref struct
    where T6 : allows ref struct
    where T7 : allows ref struct
    where T8 : allows ref struct
{
    private readonly Archetype _archetype;

    private Columns(Archetype archetype)
    {
        _archetype = archetype;
    }

    public int Count => _archetype.Count;

    public T1 Item1 => QueryTerm<T1>.Instance!.Create(_archetype);

    public T2 Item2 => QueryTerm<T2>.Instance!.Create(_archetype);

    public T3 Item3 => QueryTerm<T3>.Instance!.Create(_archetype);

    public T4 Item4 => QueryTerm<T4>.Instance!.Create(_archetype);

    public T5 Item5 => QueryTerm<T5>.Instance!.Create(_archetype);

    public T6 Item6 => QueryTerm<T6>.Instance!.Create(_archetype);

    public T7 Item7 => QueryTerm<T7>.Instance!.Create(_archetype);

    public T8 Item8 => QueryTerm<T8>.Instance!.Create(_archetype);

    public void Deconstruct(out T1 item1, out T2 item2, out T3 item3, out T4 item4, out T5 item5, out T6 item6, out T7 item7, out T8 item8)
    {
        item1 = QueryTerm<T1>.Instance!.Create(_archetype);
        item2 = QueryTerm<T2>.Instance!.Create(_archetype);
        item3 = QueryTerm<T3>.Instance!.Create(_archetype);
        item4 = QueryTerm<T4>.Instance!.Create(_archetype);
        item5 = QueryTerm<T5>.Instance!.Create(_archetype);
        item6 = QueryTerm<T6>.Instance!.Create(_archetype);
        item7 = QueryTerm<T7>.Instance!.Create(_archetype);
        item8 = QueryTerm<T8>.Instance!.Create(_archetype);
    }

    static Columns<T1, T2, T3, T4, T5, T6, T7, T8> IColumns<Columns<T1, T2, T3, T4, T5, T6, T7, T8>>.Create(Archetype archetype) => new Columns<T1, T2, T3, T4, T5, T6, T7, T8>(archetype);
}
