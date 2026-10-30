using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Pixely.Collections;

namespace Pixely.Ecs;

// A component that points its entity, the source, at another entity, the target. The world keeps the sources of each target, which
// RelationshipSources<T> reads without scanning. It changes only through ComponentAdder<T>, ComponentRemover<T> and spawners, which apply at sync points
// where the world updates its index, so a query writes no Span<T> of it and a system takes no ComponentLookup<...> of it.
// Destroying the target removes the relationship from its sources, or destroys them when it implements IDestroyedWithTarget.
public interface IRelationship
{
    Entity Target { get; }
}

// A relationship whose sources are destroyed with their target, as a sprite attached to a body is.
public interface IDestroyedWithTarget : IRelationship;

// The Pixely.Ecs generator registers every struct that implements IRelationship in an assembly, when the assembly loads. The world cannot read the target
// of a component it knows only as an unconstrained T without boxing it, so it reads targets through the relationships registered here.
// The method is public only because the generated module initializer lives in the game's assembly.
public static class Relationships
{
    public static void Register<T>() where T : struct, IRelationship => RelationshipType<T>.Instance ??= new RegisteredRelationship<T>();
}

// How the world reads and removes one relationship component, whatever its type.
internal abstract class RelationshipType
{
    private static readonly ConcurrentDictionary<int, RelationshipType> ByComponentId = new ConcurrentDictionary<int, RelationshipType>();

    private protected RelationshipType(int componentId, string name, bool destroyedWithTarget)
    {
        ComponentId = componentId;
        Name = name;
        DestroyedWithTarget = destroyedWithTarget;
        ByComponentId[componentId] = this;
    }

    public int ComponentId { get; }

    public string Name { get; }

    public bool DestroyedWithTarget { get; }

    public static RelationshipType? Find(int componentId) => ByComponentId.GetValueOrDefault(componentId);

    // The target of the source in the row of the archetype, whose column holds the relationship.
    public abstract Entity GetTarget(Archetype archetype, int column, int row);

    public abstract void Remove(World world, Entity source);
}

// Unconstrained, so the world reaches the registered relationship from its generic members.
internal abstract class RelationshipType<T> : RelationshipType where T : struct
{
    public static RelationshipType<T>? Instance;

    private protected RelationshipType() : base(ComponentType<T>.Id, ComponentType<T>.Name, default(T) is IDestroyedWithTarget)
    {
    }

    public abstract Entity GetTarget(in T component);

    public override Entity GetTarget(Archetype archetype, int column, int row) => GetTarget(in archetype.GetColumnUnchecked<T>(column).GetUnchecked(row));

    public override void Remove(World world, Entity source) => world.Remove<T>(source);
}

internal sealed class RegisteredRelationship<T> : RelationshipType<T> where T : struct, IRelationship
{
    public override Entity GetTarget(in T component) => component.Target;
}

// A column of an archetype that holds a relationship.
internal readonly record struct RelationshipColumn(int Column, RelationshipType Type);

// The sources of one relationship by target. A source has the relationship once, so it has one target and one position in that target's sources.
internal sealed class RelationshipIndex
{
    private readonly Dictionary<Entity, FastList<Entity>> _sourcesByTarget = [];
    private readonly Dictionary<Entity, int> _positions = [];

    public RelationshipIndex(RelationshipType type)
    {
        Type = type;
    }

    public RelationshipType Type { get; }

    public ReadOnlySpan<Entity> GetSources(Entity target) => _sourcesByTarget.TryGetValue(target, out FastList<Entity>? sources) ? sources.AsReadOnlySpan() : default;

    public void Add(Entity source, Entity target)
    {
        ref FastList<Entity>? sources = ref CollectionsMarshal.GetValueRefOrAddDefault(_sourcesByTarget, target, out _);
        sources ??= new FastList<Entity>(4);
        _positions.Add(source, sources.Length);
        sources.Add(source);
    }

    // Moves the target's last source into the removed position, so the sources of a target are in no particular order. A source that TakeSources
    // already forgot is skipped, because its target was destroyed.
    public void Remove(Entity source, Entity target)
    {
        if (!_positions.Remove(source, out int position))
        {
            return;
        }

        FastList<Entity> sources = _sourcesByTarget[target];
        Entity last = sources[sources.LastIndex];
        sources[position] = last;
        sources.RemoveLast();
        if (last != source)
        {
            _positions[last] = position;
        }

        if (sources.Length == 0)
        {
            _sourcesByTarget.Remove(target);
        }
    }

    // Forgets the sources of a destroyed target and returns them, for the world to remove the relationship from them or destroy them.
    public FastList<Entity>? TakeSources(Entity target)
    {
        if (!_sourcesByTarget.Remove(target, out FastList<Entity>? sources))
        {
            return null;
        }

        foreach (Entity source in sources.AsReadOnlySpan())
        {
            _positions.Remove(source);
        }

        return sources;
    }
}
