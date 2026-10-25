using System.Diagnostics.CodeAnalysis;

namespace Pixely.Ecs;

// Reads and writes T of a given entity. Structural changes wait for the sync point, so a reference stays valid until the system's Update returns.
public sealed class ComponentLookup<T> : ILookup where T : struct
{
    // Set only while the system's Update runs, so the check that it runs is a null test of the world each access loads anyway.
    private World? _world;

    internal ComponentLookup(CommandBuffer commands)
    {
        commands.AddLookup(this);
    }

    // False for an entity that is not alive, unlike World.Has, so it guards the indexer for an entity that may have been destroyed.
    public bool Has(Entity entity) => (_world ?? Lookup.ThrowOutsideUpdate()).IsAliveWith<T>(entity);

    // Throws for an entity that is not alive, such as one spawned in this update, or one without T. For a tag it is one value that all entities share.
    public ref T this[Entity entity] => ref (_world ?? Lookup.ThrowOutsideUpdate()).Get<T>(entity);

    // The indexer without its checks, for lookups in hot loops, with the undefined behavior of World.UnsafeGet. Outside Update it throws NullReferenceException.
    public ref T UnsafeGet(Entity entity) => ref _world!.UnsafeGet<T>(entity);

    void ILookup.SetWorld(World? world) => _world = world;
}

// Reads T of a given entity. Structural changes wait for the sync point, so a reference stays valid until the system's Update returns.
public sealed class ReadOnlyComponentLookup<T> : ILookup where T : struct
{
    // Set only while the system's Update runs, so the check that it runs is a null test of the world each access loads anyway.
    private World? _world;

    internal ReadOnlyComponentLookup(CommandBuffer commands)
    {
        commands.AddLookup(this);
    }

    // False for an entity that is not alive, unlike World.Has, so it guards the indexer for an entity that may have been destroyed.
    public bool Has(Entity entity) => (_world ?? Lookup.ThrowOutsideUpdate()).IsAliveWith<T>(entity);

    // Throws for an entity that is not alive, such as one spawned in this update, or one without T. For a tag it is one value that all entities share.
    public ref readonly T this[Entity entity] => ref (_world ?? Lookup.ThrowOutsideUpdate()).Get<T>(entity);

    // The indexer without its checks, for lookups in hot loops, with the undefined behavior of World.UnsafeGet. Outside Update it throws NullReferenceException.
    public ref readonly T UnsafeGet(Entity entity) => ref _world!.UnsafeGet<T>(entity);

    void ILookup.SetWorld(World? world) => _world = world;
}

// A lookup gets its world when its system's Update starts and loses it when Update ends, so a lookup kept past Update throws.
internal interface ILookup
{
    void SetWorld(World? world);
}

internal static class Lookup
{
    public static bool Has<T>(Archetype archetype) where T : struct => archetype.GetColumnIndex(ComponentType<T>.Id) != Archetype.Absent;

    [DoesNotReturn]
    public static World ThrowOutsideUpdate() => throw new InvalidOperationException("A lookup reads components only during its system's Update.");
}
