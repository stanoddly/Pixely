namespace Pixely.Ecs;

// What a query requires, reads as optional and excludes, as its span type arguments and its IWith<...> and IWithout<...> describe it.
internal sealed class QueryDescription : IFilterDescription
{
    private readonly string _subject;
    private readonly List<int> _read = [];
    private readonly List<int> _required = [];
    private readonly List<int> _optional = [];
    private readonly List<int> _excluded = [];
    private bool _writes;

    public QueryDescription(string subject)
    {
        _subject = subject;
    }

    // A span of T, which the entity must have.
    public void Read<T>() where T : struct
    {
        AddRead<T>();
        _required.Add(ComponentType<T>.Id);
    }

    // A Span<T>, which the query may write.
    public void Write<T>() where T : struct
    {
        Read<T>();
        _writes = true;
    }

    // An Optional<ReadOnlySpan<T>>, so the entity may lack T.
    public void ReadOptional<T>() where T : struct
    {
        AddRead<T>();
        _optional.Add(ComponentType<T>.Id);
    }

    // An Optional<Span<T>>, which the query may write where the entity has T.
    public void WriteOptional<T>() where T : struct
    {
        ReadOptional<T>();
        _writes = true;
    }

    public void With<T>() where T : struct => _required.Add(ComponentType<T>.Id);

    public void Without<T>() where T : struct => _excluded.Add(ComponentType<T>.Id);

    public QueryState CreateState()
    {
        ComponentSet required = ComponentSet.From(_required);
        if (required.Ids.IsEmpty)
        {
            throw new InvalidOperationException($"{_subject} must require at least one component through Span<T>, ReadOnlySpan<T> or IWith<...>.");
        }

        ComponentSet excluded = ComponentSet.From(_excluded);
        if (required.ContainsAny(excluded))
        {
            throw new InvalidOperationException($"{_subject} both requires and excludes a component, so it matches no entity.");
        }

        ComponentSet optional = ComponentSet.From(_optional);
        if (optional.ContainsAny(required) || optional.ContainsAny(excluded))
        {
            throw new InvalidOperationException($"{_subject} reads a component as optional that IWith<...> or IWithout<...> also names, so it is always present or always absent.");
        }

        return new QueryState(required, optional, excluded, _writes);
    }

    private void AddRead<T>() where T : struct
    {
        if (ColumnType<T>.Instance.IsTag)
        {
            throw new InvalidOperationException($"{_subject} reads {ComponentType<T>.Name}, a tag, which has no data. Filter on it with IWith<{ComponentType<T>.Name}> or IWithout<{ComponentType<T>.Name}> instead.");
        }

        int id = ComponentType<T>.Id;
        if (_read.Contains(id))
        {
            throw new InvalidOperationException($"{_subject} reads {ComponentType<T>.Name} in more than one span.");
        }

        _read.Add(id);
    }
}
