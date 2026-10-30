namespace Pixely.Ecs;

// The sources of the relationship T that point at a given target, which the world indexes, so reading them scans no entities. The span is in no
// particular order, empty for a target without sources or one that is not alive, and valid until the next structural change. A system's lookup reads only
// during its Update; one that AddRelationshipSources registers for code outside systems reads at any time.
public sealed class RelationshipSources<T> : ILookup where T : struct, IRelationship
{
    private World? _world;

    internal RelationshipSources(CommandBuffer commands)
    {
        commands.AddLookup(this);
    }

    internal RelationshipSources(World world)
    {
        _world = world;
    }

    public ReadOnlySpan<Entity> this[Entity target] => (_world ?? Lookup.ThrowOutsideUpdate()).GetSources(ComponentType<T>.Id, target);

    void ILookup.SetWorld(World? world) => _world = world;
}
