namespace Pixely.Ecs;

// One chunk of an archetype's entities, with each of their components in one span. A chunk holds a power of two of rows, at least 64;
// only an archetype's last chunk can hold fewer.
public readonly struct ArchetypeChunk
{
    private readonly Archetype _archetype;
    private readonly int _index;

    internal ArchetypeChunk(Archetype archetype, int index)
    {
        _archetype = archetype;
        _index = index;
    }

    public int Count => _archetype.GetChunkCount(_index);

    public ReadOnlySpan<Entity> Entities => _archetype.GetChunkEntities(_index);

    public bool Has<T>() where T : struct => _archetype.GetColumnIndex(ComponentType<T>.Id) >= 0;

    public Span<T> GetSpan<T>() where T : struct
    {
        int column = _archetype.GetColumnIndex(ComponentType<T>.Id);
        if (column < 0)
        {
            throw new InvalidOperationException($"The archetype has no {ComponentType<T>.Name} component.");
        }

        return _archetype.GetColumnUnchecked<T>(column).GetChunk(_index, Count);
    }
}
