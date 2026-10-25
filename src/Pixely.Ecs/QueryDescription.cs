namespace Pixely.Ecs;

// What a query requires and excludes, as its span type arguments and its IWith<...> and IWithout<...> describe it.
internal sealed class QueryDescription : IFilterDescription
{
    private readonly string _subject;
    private readonly List<int> _read = [];
    private readonly List<int> _required = [];
    private readonly List<int> _excluded = [];

    public QueryDescription(string subject)
    {
        _subject = subject;
    }

    // A span of T, which the entity must have.
    public void Read<T>() where T : struct
    {
        if (ColumnType<T>.Instance.IsTag)
        {
            throw new InvalidOperationException($"{_subject} reads {ComponentType<T>.Name}, a tag, which has no data. Use IWith<{ComponentType<T>.Name}> instead.");
        }

        int id = ComponentType<T>.Id;
        if (_read.Contains(id))
        {
            throw new InvalidOperationException($"{_subject} reads {ComponentType<T>.Name} in more than one span.");
        }

        _read.Add(id);
        _required.Add(id);
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

        return new QueryState(required, excluded);
    }
}
