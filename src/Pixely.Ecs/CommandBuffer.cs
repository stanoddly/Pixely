using System.Runtime.InteropServices;
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

// A spawner's request for room in its archetype, which the buffer makes before the system's commands.
internal interface IRoomRequest
{
    int Room { get; }

    Archetype GetArchetype(World world);
}

// One system's structural changes, recorded by its writers and applied in recording order at a sync point.
internal sealed class CommandBuffer
{
    // Consecutive commands from one writer share an entry, so a run of spawns keeps the log short.
    private struct Command
    {
        public IRecordedCommands Source;
        public int First;
        public int Count;
    }

    private readonly FastList<Command> _commands = new FastList<Command>(4);
    private readonly List<IRecordedCommands> _sources = [];
    private readonly List<IRoomRequest> _roomRequests = [];
    private readonly Dictionary<Archetype, int> _roomByArchetype = [];

    public CommandBuffer(World world)
    {
        World = world;
    }

    public World World { get; }

    public bool IsRecording { get; set; }

    public void ThrowIfNotRecording()
    {
        if (!IsRecording)
        {
            throw new InvalidOperationException("A writer records changes only during its system's Update.");
        }
    }

    public void AddSource(IRecordedCommands source) => _sources.Add(source);

    public void Record(IRecordedCommands source, int index)
    {
        ThrowIfNotRecording();
        if (_commands.Length > 0)
        {
            ref Command last = ref _commands[_commands.LastIndex];
            if (ReferenceEquals(last.Source, source) && last.First + last.Count == index)
            {
                last.Count++;
                return;
            }
        }

        _commands.Add(new Command { Source = source, First = index, Count = 1 });
    }

    public void RequestRoom(IRoomRequest request) => _roomRequests.Add(request);

    // Hands each run of consecutive commands from one writer to it at once, so a run of spawns becomes one batch.
    // Clears the buffer even when a command throws, so a later sync point does not apply the commands before it twice.
    public void Apply()
    {
        int applied = 0;
        try
        {
            MakeRequestedRoom();
            for (; applied < _commands.Length; applied++)
            {
                Command command = _commands[applied];
                command.Source.Apply(World, command.First, command.Count);
            }
        }
        finally
        {
            DiscardFrom(applied);
        }
    }

    // Sums the requests for each archetype before growing it, so the room covers every spawn of the system, whatever the order of its commands.
    // Entities the system destroys in the same archetype are not subtracted, so the archetype can grow a little more than needed.
    private void MakeRequestedRoom()
    {
        if (_roomRequests.Count == 0)
        {
            return;
        }

        foreach (IRoomRequest request in _roomRequests)
        {
            ref int room = ref CollectionsMarshal.GetValueRefOrAddDefault(_roomByArchetype, request.GetArchetype(World), out _);
            room = checked(room + request.Room);
        }

        foreach ((Archetype archetype, int room) in _roomByArchetype)
        {
            World.EnsureRoom(archetype, room);
        }
    }

    // Drops every command, when an earlier command of the sync point threw or a system's Update threw before it.
    public void Discard() => DiscardFrom(0);

    private void DiscardFrom(int first)
    {
        for (int i = first; i < _commands.Length; i++)
        {
            Command command = _commands[i];
            for (int index = command.First; index < command.First + command.Count; index++)
            {
                command.Source.Discard(World, index);
            }
        }

        _commands.Resize(0);
        _roomRequests.Clear();
        _roomByArchetype.Clear();
        foreach (IRecordedCommands source in _sources)
        {
            source.Clear();
        }
    }
}
