using Pixely.Collections;

namespace Pixely.Ecs;

// Adds T at the next sync point. The value replaces T when the entity has it by then, and a dead entity is skipped.
public sealed class ComponentAdder<T> : IRecordedCommands where T : struct
{
    private readonly CommandBuffer _commands;
    private readonly ChunkedList<Entity> _entities = new ChunkedList<Entity>();
    private readonly ChunkedList<T> _values = new ChunkedList<T>();

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

    void IRecordedCommands.Clear()
    {
        _entities.Clear();
        _values.Clear();
    }
}

// Removes T at the next sync point. An entity without T by then, or a dead one, is skipped.
public sealed class ComponentRemover<T> : IRecordedCommands where T : struct
{
    private readonly CommandBuffer _commands;
    private readonly ChunkedList<Entity> _entities = new ChunkedList<Entity>();

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

    void IRecordedCommands.Clear() => _entities.Clear();
}

// Destroys entities at the next sync point. An entity that is dead by then is skipped.
public sealed class EntityDestroyer : IRecordedCommands
{
    private readonly CommandBuffer _commands;
    private readonly ChunkedList<Entity> _entities = new ChunkedList<Entity>();

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

    void IRecordedCommands.Clear() => _entities.Clear();
}

// Creates entities from a blueprint at the next sync point, directly in the archetype of its components.
public sealed class Spawner<TBlueprint> : IRecordedCommands, IRoomRequest where TBlueprint : struct, IBlueprint<TBlueprint>
{
    private readonly CommandBuffer _commands;
    private readonly ChunkedList<Entity> _entities = new ChunkedList<Entity>();
    private readonly ChunkedList<TBlueprint> _values = new ChunkedList<TBlueprint>();
    private int _room;

    internal Spawner(CommandBuffer commands)
    {
        _commands = commands;
        commands.AddSource(this);
    }

    // Grows the archetype at the sync point, before any of the system's commands, so it never replaces the arrays under a running query.
    // The buffer sums the requests of the system for each archetype, so spawners that share one each get room for their own spawns. The room
    // is rows, not spawns: entities the system's other writers move into the archetype fill it too. A system that throws drops the request with
    // its other commands.
    public void EnsureRoomFor(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _commands.ThrowIfNotRecording();
        if (count == 0)
        {
            return;
        }

        if (_room == 0)
        {
            _commands.RequestRoom(this);
        }

        _room = checked(_room + count);
    }

    // The entity is reserved now and becomes alive at the sync point, so other commands of the same system can already target it.
    public Entity Spawn(in TBlueprint blueprint)
    {
        _commands.ThrowIfNotRecording();
        Entity entity = _commands.World.Reserve();
        _values.Add(blueprint);
        _commands.Record(this, _entities.Add(entity));
        return entity;
    }

    void IRecordedCommands.Apply(World world, int first, int count) => world.Spawn(_entities, _values, first, count);

    void IRecordedCommands.Discard(World world, int index) => world.ReleaseReservation(_entities[index]);

    void IRecordedCommands.Clear()
    {
        _entities.Clear();
        _values.Clear();
        _room = 0;
    }

    int IRoomRequest.Room => _room;

    // Resolved at the sync point, because creating the archetype while a query runs would change the archetypes it enumerates.
    Archetype IRoomRequest.GetArchetype(World world) => world.GetBlueprintState<TBlueprint>().Archetype;
}
