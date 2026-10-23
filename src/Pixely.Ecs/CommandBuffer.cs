using Pixely.Collections;

namespace Pixely.Ecs;

// One writer's commands. Each command is an index into the writer's own lists, in the order it recorded them.
internal interface IRecordedCommands
{
    // Applies the commands from first to first + count - 1, which the buffer recorded one after another.
    void Apply(World world, int first, int count);

    // Called for a command that did not run because an earlier one threw.
    void Discard(World world, int index)
    {
    }

    void Clear();
}

// One system's structural changes, recorded by its writers and applied in recording order at a sync point.
internal sealed class CommandBuffer
{
    private readonly record struct Command(IRecordedCommands Source, int Index);

    private readonly FastList<Command> _commands = new FastList<Command>(4);
    private readonly List<IRecordedCommands> _sources = [];

    public CommandBuffer(World world)
    {
        World = world;
    }

    public World World { get; }

    public void AddSource(IRecordedCommands source) => _sources.Add(source);

    public void Record(IRecordedCommands source, int index) => _commands.Add(new Command(source, index));

    // Hands each run of consecutive commands from one writer to it at once, so a run of spawns becomes one batch.
    // Clears the buffer even when a command throws, so a later sync point does not apply the commands before it twice.
    public void Apply()
    {
        int applied = 0;
        try
        {
            ReadOnlySpan<Command> commands = _commands.AsReadOnlySpan();
            while (applied < commands.Length)
            {
                Command first = commands[applied];
                int count = 1;
                while (applied + count < commands.Length && ReferenceEquals(commands[applied + count].Source, first.Source))
                {
                    count++;
                }

                first.Source.Apply(World, first.Index, count);
                applied += count;
            }
        }
        finally
        {
            for (int i = applied; i < _commands.Length; i++)
            {
                _commands[i].Source.Discard(World, _commands[i].Index);
            }

            _commands.Resize(0);
            foreach (IRecordedCommands source in _sources)
            {
                source.Clear();
            }
        }
    }
}
