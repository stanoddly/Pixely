namespace Pixely.Ecs;

// Sorted, distinct component ids. Identifies an archetype and the filters of a query.
internal readonly struct ComponentSet : IEquatable<ComponentSet>
{
    private readonly int[] _ids;

    private ComponentSet(int[] ids)
    {
        _ids = ids;
    }

    public static ComponentSet Empty { get; } = new ComponentSet([]);

    public ReadOnlySpan<int> Ids => _ids;

    public static ComponentSet From(IEnumerable<int> ids) => new ComponentSet(ids.Distinct().Order().ToArray());

    public int IndexOf(int id)
    {
        int index = Array.BinarySearch(_ids, id);
        return index < 0 ? -1 : index;
    }

    public bool Contains(int id) => IndexOf(id) >= 0;

    public bool ContainsAll(ComponentSet other)
    {
        foreach (int id in other._ids)
        {
            if (!Contains(id))
            {
                return false;
            }
        }

        return true;
    }

    public bool ContainsAny(ComponentSet other)
    {
        foreach (int id in other._ids)
        {
            if (Contains(id))
            {
                return true;
            }
        }

        return false;
    }

    public ComponentSet With(int id) => From(_ids.Append(id));

    public ComponentSet Without(int id) => new ComponentSet(_ids.Where(existing => existing != id).ToArray());

    public bool Equals(ComponentSet other) => _ids.AsSpan().SequenceEqual(other._ids);

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        foreach (int id in _ids)
        {
            hash.Add(id);
        }

        return hash.ToHashCode();
    }
}
