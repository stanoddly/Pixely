using Pixely.Collections;

namespace Pixely.Ecs;

// Updates its systems in the order they were added and applies their structural changes at sync points.
// A sync point runs before a system that names a component an earlier system changed since the last one, and after the last system.
public sealed class SystemScheduler : IUpdatable
{
    private sealed class SystemEntry
    {
        public SystemEntry(object system, Action update, SystemContract contract, CommandBuffer commands)
        {
            System = system;
            Update = update;
            Contract = contract;
            Commands = commands;
        }

        public object System { get; }

        public Action Update { get; }

        public SystemContract Contract { get; }

        public CommandBuffer Commands { get; }

        public bool SyncBefore { get; set; }

        public bool Removed { get; set; }
    }

    private readonly World _world;
    private readonly FastList<SystemEntry> _entries = new FastList<SystemEntry>(4);
    private bool _planIsStale;

    public SystemScheduler(World world)
    {
        _world = world;
    }

    // Adds an ISystem. For an ISystem<...>, the Pixely.Ecs generator intercepts this call and passes the queries and writers of its type arguments.
    // A system added during Update runs from the next Update.
    public TSystem Add<TSystem>(TSystem system) where TSystem : class
    {
        if (system is not ISystem)
        {
            throw new InvalidOperationException(
                $"Add({typeof(TSystem).Name}) was not intercepted by the source generator, which an ISystem<...> needs. Reference Pixely.Ecs.Generator as an analyzer and add Pixely.Ecs.Generated to InterceptorsNamespaces.");
        }

        return Add(system, static _ => static system => ((ISystem)system).Update());
    }

    // bind creates the system's queries and writers through the builder, which records them as its contract, and returns the call of its Update with them.
    // The generated code of Add and AddSystem calls it.
    public TSystem Add<TSystem>(TSystem system, Func<SystemBuilder, Action<TSystem>> bind) where TSystem : class
    {
        SystemBuilder builder = new SystemBuilder(_world);
        Action<TSystem> update;
        try
        {
            update = bind(builder);
        }
        finally
        {
            builder.Close();
        }

        _entries.Add(new SystemEntry(system, () => update(system), builder.Contract, builder.Commands));
        _planIsStale = true;
        return system;
    }

    // A system removed during Update still runs in that Update and leaves from the next one.
    public void Remove(object system)
    {
        foreach (SystemEntry entry in _entries.AsReadOnlySpan())
        {
            if (ReferenceEquals(entry.System, system))
            {
                entry.Removed = true;
                _planIsStale = true;
                return;
            }
        }
    }

    public void Update()
    {
        if (_planIsStale)
        {
            Plan();
        }

        int count = _entries.Length;
        int segmentStart = 0;
        for (int i = 0; i < count; i++)
        {
            SystemEntry entry = _entries[i];
            if (entry.SyncBefore)
            {
                ApplyCommands(segmentStart, i);
                segmentStart = i;
            }

            // A writer records only while its system updates, so one kept past Update throws instead of applying its change at an unexpected sync point.
            entry.Commands.IsRecording = true;
            try
            {
                entry.Update();
            }
            finally
            {
                entry.Commands.IsRecording = false;
            }
        }

        ApplyCommands(segmentStart, count);
    }

    // When a command throws, its buffer drops the commands after it and the later systems' buffers are dropped, so no later sync point applies them.
    private void ApplyCommands(int start, int end)
    {
        int i = start;
        try
        {
            for (; i < end; i++)
            {
                _entries[i].Commands.Apply();
            }
        }
        finally
        {
            for (int later = i + 1; later < end; later++)
            {
                _entries[later].Commands.Discard();
            }
        }
    }

    // Drops removed systems and places the sync points. For a fixed order, a sync point as late as possible gives the fewest.
    private void Plan()
    {
        int kept = 0;
        for (int i = 0; i < _entries.Length; i++)
        {
            SystemEntry entry = _entries[i];
            if (!entry.Removed)
            {
                _entries[kept++] = entry;
            }
        }

        _entries.AsSpan()[kept..].Clear();
        _entries.Resize(kept);

        HashSet<int> changed = [];
        bool destroyed = false;
        foreach (SystemEntry entry in _entries.AsReadOnlySpan())
        {
            SystemContract contract = entry.Contract;
            entry.SyncBefore = (destroyed && contract.Touched.Count > 0) || changed.Overlaps(contract.Touched);
            if (entry.SyncBefore)
            {
                changed.Clear();
                destroyed = false;
            }

            changed.UnionWith(contract.Changed);
            destroyed |= contract.Destroys;
        }

        _planIsStale = false;
    }
}
