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

    // Checks before storing anything, so a call outside Update that the caller catches leaves nothing behind.
    public void Add(Entity entity, T component)
    {
        _commands.ThrowIfNotRecording();
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
                world.Replace(entity, _values[i]);
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

    public void Remove(Entity entity)
    {
        _commands.ThrowIfNotRecording();
        _commands.Record(this, _entities.Add(entity));
    }

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

    public void Destroy(Entity entity)
    {
        _commands.ThrowIfNotRecording();
        _commands.Record(this, _entities.Add(entity));
    }

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
