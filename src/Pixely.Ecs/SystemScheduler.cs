using Pixely.Collections;

namespace Pixely.Ecs;

// Updates its systems in the order they were added and applies their structural changes at sync points.
// A sync point runs before a system that names a component an earlier system changed since the last one, and after the last system.
public sealed class SystemScheduler : IUpdatable
{
    private sealed class SystemEntry
    {
        public SystemEntry(ISystem system, SystemContract contract, CommandBuffer commands)
        {
            System = system;
            Contract = contract;
            Commands = commands;
        }

        public ISystem System { get; }

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

    // A system added during Update runs from the next Update.
    public TSystem Add<TSystem>(Func<SystemBuilder, TSystem> create) where TSystem : ISystem
    {
        SystemBuilder builder = new SystemBuilder(_world);
        TSystem system;
        try
        {
            system = create(builder);
        }
        finally
        {
            builder.Close();
        }

        _entries.Add(new SystemEntry(system, builder.Contract, builder.Commands));
        _planIsStale = true;
        return system;
    }

    // A system removed during Update is skipped from then on; the commands it already recorded still apply.
    public void Remove(ISystem system)
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

            if (!entry.Removed)
            {
                entry.System.Update();
            }
        }

        ApplyCommands(segmentStart, count);
    }

    private void ApplyCommands(int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            _entries[i].Commands.Apply();
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
