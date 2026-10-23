using Pixely.Collections;

namespace Pixely.Ecs;

// Adds T at the next sync point. The value replaces T when the entity has it by then, and a dead entity is skipped.
public sealed class ComponentAdder<T> : IRecordedCommands where T : struct
{
    private readonly CommandBuffer _commands;
    private readonly FastList<Entity> _entities = new FastList<Entity>(4);
    private readonly FastList<T> _values = new FastList<T>(4);

    internal ComponentAdder(CommandBuffer commands)
    {
        _commands = commands;
        commands.AddSource(this);
    }

    public void Add(Entity entity, T component)
    {
        _values.Add(component);
        _commands.Record(this, _entities.Add(entity));
    }

    void IRecordedCommands.Apply(World world, int first, int count)
    {
        for (int i = first; i < first + count; i++)
        {
            Entity entity = _entities[i];
            if (!world.IsAlive(entity))
            {
                continue;
            }

            if (world.Has<T>(entity))
            {
                world.Get<T>(entity) = _values[i];
            }
            else
            {
                world.Add(entity, _values[i]);
            }
        }
    }

    // Resize keeps the old values in the array, so clear them first to release what they reference.
    void IRecordedCommands.Clear()
    {
        _entities.Resize(0);
        _values.AsSpan().Clear();
        _values.Resize(0);
    }
}

// Removes T at the next sync point. An entity without T by then, or a dead one, is skipped.
public sealed class ComponentRemover<T> : IRecordedCommands where T : struct
{
    private readonly CommandBuffer _commands;
    private readonly FastList<Entity> _entities = new FastList<Entity>(4);

    internal ComponentRemover(CommandBuffer commands)
    {
        _commands = commands;
        commands.AddSource(this);
    }

    public void Remove(Entity entity) => _commands.Record(this, _entities.Add(entity));

    void IRecordedCommands.Apply(World world, int first, int count)
    {
        for (int i = first; i < first + count; i++)
        {
            Entity entity = _entities[i];
            if (world.IsAlive(entity) && world.Has<T>(entity))
            {
                world.Remove<T>(entity);
            }
        }
    }

    void IRecordedCommands.Clear() => _entities.Resize(0);
}

// Destroys entities at the next sync point. An entity that is dead by then is skipped.
public sealed class EntityDestroyer : IRecordedCommands
{
    private readonly CommandBuffer _commands;
    private readonly FastList<Entity> _entities = new FastList<Entity>(4);

    internal EntityDestroyer(CommandBuffer commands)
    {
        _commands = commands;
        commands.AddSource(this);
    }

    public void Destroy(Entity entity) => _commands.Record(this, _entities.Add(entity));

    void IRecordedCommands.Apply(World world, int first, int count)
    {
        for (int i = first; i < first + count; i++)
        {
            Entity entity = _entities[i];
            if (world.IsAlive(entity))
            {
                world.Destroy(entity);
            }
        }
    }

    void IRecordedCommands.Clear() => _entities.Resize(0);
}

// Creates entities from a blueprint at the next sync point, directly in the archetype of its components.
public sealed class Spawner<TBlueprint> : IRecordedCommands where TBlueprint : struct, IBlueprint<TBlueprint>
{
    private readonly CommandBuffer _commands;
    private readonly FastList<Entity> _entities = new FastList<Entity>(4);
    private readonly FastList<TBlueprint> _values = new FastList<TBlueprint>(4);

    internal Spawner(CommandBuffer commands)
    {
        _commands = commands;
        commands.AddSource(this);
    }

    // The entity is reserved now and becomes alive at the sync point, so other commands of the same system can already target it.
    public Entity Spawn(in TBlueprint blueprint)
    {
        Entity entity = _commands.World.Reserve();
        _values.Add(blueprint);
        _commands.Record(this, _entities.Add(entity));
        return entity;
    }

    void IRecordedCommands.Apply(World world, int first, int count)
    {
        world.Spawn(_entities.AsReadOnlySpan().Slice(first, count), _values.AsReadOnlySpan().Slice(first, count));
    }

    void IRecordedCommands.Discard(World world, int index) => world.ReleaseReservation(_entities[index]);

    void IRecordedCommands.Clear()
    {
        _entities.Resize(0);
        _values.AsSpan().Clear();
        _values.Resize(0);
    }
}
