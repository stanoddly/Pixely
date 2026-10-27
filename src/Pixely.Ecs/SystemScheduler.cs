using Pixely.Collections;

namespace Pixely.Ecs;

// Updates its systems in the order of their order numbers, which follow the AddSystem and Add calls, and applies their structural changes at sync points.
// A sync point runs before a system that names a component an earlier system changed since the last one, and after the last system.
public sealed class SystemScheduler : IUpdatable
{
    private sealed class SystemEntry
    {
        public SystemEntry(ISystem system, long order, SystemContract contract, CommandBuffer commands)
        {
            System = system;
            Order = order;
            Contract = contract;
            Commands = commands;
        }

        public ISystem System { get; }

        public long Order { get; }

        public SystemContract Contract { get; }

        public CommandBuffer Commands { get; }

        public bool SyncBefore { get; set; }

        public bool Removed { get; set; }
    }

    private static long s_lastOrder;

    private readonly World _world;
    private readonly FastList<SystemEntry> _entries = new FastList<SystemEntry>(4);
    private bool _planIsStale;

    public SystemScheduler(World world)
    {
        _world = world;
    }

    // Adds a system created without queries, lookups or writers, so its contract is empty. A system added during Update runs from the next Update.
    public TSystem Add<TSystem>(TSystem system) where TSystem : class, ISystem => Add(_ => system, ReserveOrder());

    // Adds the system that create returns. create passes the system the queries, lookups and writers it gets from the builder, which records them as
    // the system's contract. A system added during Update runs from the next Update.
    public TSystem Add<TSystem>(Func<SystemBuilder, TSystem> create) where TSystem : class, ISystem => Add(create, ReserveOrder());

    // An order number for a system, unique in the process and larger than every earlier one. AddSystem's generated code takes it when AddSystem is called,
    // because the provider creates singletons grouped by type, and earlier when another service needs one, so creation order is not call order.
    public static long ReserveOrder() => Interlocked.Increment(ref s_lastOrder);

    // As Add(create), but the system runs among the others by its order from ReserveOrder. The generated code of AddSystem calls it.
    public TSystem Add<TSystem>(Func<SystemBuilder, TSystem> create, long order) where TSystem : class, ISystem
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

        _entries.Add(new SystemEntry(system, order, builder.Contract, builder.Commands));
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

            // A writer records only while its system updates, so one used in the constructor or kept for a callback throws instead of applying its change at
            // an unexpected sync point.
            // When a system throws, the commands recorded since the last sync point are dropped, so no later sync point applies them, and a spawn's
            // reserved entity is released.
            entry.Commands.IsRecording = true;
            try
            {
                entry.System.Update();
            }
            catch
            {
                for (int recorded = segmentStart; recorded <= i; recorded++)
                {
                    _entries[recorded].Commands.Discard();
                }

                throw;
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

    // Drops removed systems, sorts the rest by order and places the sync points. For a fixed order, a sync point as late as possible gives the fewest.
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

        // Add appends, so that a system added during Update leaves the running loop alone. Order numbers are unique, so the order is total.
        _entries.AsSpan().Sort(static (left, right) => left.Order.CompareTo(right.Order));

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
