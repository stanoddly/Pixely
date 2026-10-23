using Pixely.Collections;

namespace Pixely.Ecs;

// The typed values behind one writer's commands.
internal interface IRecordedCommands
{
    void Apply(World world, Entity entity, int valueIndex);

    // Called for a command that did not run because an earlier one threw.
    void Discard(World world, Entity entity)
    {
    }

    void Clear();
}

// One system's structural changes, recorded by its writers and applied in recording order at a sync point.
internal sealed class CommandBuffer
{
    private readonly record struct Command(Entity Entity, IRecordedCommands Source, int ValueIndex);

    private readonly FastList<Command> _commands = new FastList<Command>(4);
    private readonly List<IRecordedCommands> _sources = [];

    public CommandBuffer(World world)
    {
        World = world;
    }

    public World World { get; }

    public void AddSource(IRecordedCommands source) => _sources.Add(source);

    public void Record(Entity entity, IRecordedCommands source, int valueIndex) => _commands.Add(new Command(entity, source, valueIndex));

    // Clears the buffer even when a command throws, so a later sync point does not apply the commands before it twice.
    public void Apply()
    {
        int applied = 0;
        try
        {
            for (; applied < _commands.Length; applied++)
            {
                Command command = _commands[applied];
                command.Source.Apply(World, command.Entity, command.ValueIndex);
            }
        }
        finally
        {
            for (int i = applied; i < _commands.Length; i++)
            {
                _commands[i].Source.Discard(World, _commands[i].Entity);
            }

            _commands.Resize(0);
            foreach (IRecordedCommands source in _sources)
            {
                source.Clear();
            }
        }
    }
}
