namespace Pixely.Ecs;

// What a system's ISystem<...> declares: the components its queries and lookups name and the structural changes its writers make.
internal sealed class SystemContract
{
    public HashSet<int> Touched { get; } = [];

    public HashSet<int> Changed { get; } = [];

    public bool Destroys { get; set; }
}

// Creates the queries, lookups and writers of one system while it is added to a SystemScheduler, and records its contract.
public sealed class SystemBuilder
{
    private readonly World _world;
    private bool _closed;

    internal SystemBuilder(World world)
    {
        _world = world;
        Commands = new CommandBuffer(world);
    }

    internal CommandBuffer Commands { get; }

    internal SystemContract Contract { get; } = new SystemContract();

    public TQuery Query<TQuery>() where TQuery : Query, new()
    {
        ThrowIfClosed();
        TQuery query = _world.Query<TQuery>();
        AddAll(Contract.Touched, query.State.Required);
        AddAll(Contract.Touched, query.State.Excluded);
        return query;
    }

    public ComponentAdder<T> ComponentAdder<T>() where T : struct
    {
        ThrowIfClosed();
        Contract.Changed.Add(ComponentType<T>.Id);
        return new ComponentAdder<T>(Commands);
    }

    public ComponentRemover<T> ComponentRemover<T>() where T : struct
    {
        ThrowIfClosed();
        Contract.Changed.Add(ComponentType<T>.Id);
        return new ComponentRemover<T>(Commands);
    }

    public ComponentLookup<T> ComponentLookup<T>() where T : struct =>
        new ComponentLookup<T>(AddLookup(typeof(ComponentLookup<T>), ComponentType<T>.Id));

    public ComponentLookup<T1, T2> ComponentLookup<T1, T2>() where T1 : struct where T2 : struct =>
        new ComponentLookup<T1, T2>(AddLookup(typeof(ComponentLookup<T1, T2>), ComponentType<T1>.Id, ComponentType<T2>.Id));

    public ComponentLookup<T1, T2, T3> ComponentLookup<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct =>
        new ComponentLookup<T1, T2, T3>(AddLookup(typeof(ComponentLookup<T1, T2, T3>), ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id));

    public ComponentLookup<T1, T2, T3, T4> ComponentLookup<T1, T2, T3, T4>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
        new ComponentLookup<T1, T2, T3, T4>(AddLookup(typeof(ComponentLookup<T1, T2, T3, T4>), ComponentType<T1>.Id, ComponentType<T2>.Id,
            ComponentType<T3>.Id, ComponentType<T4>.Id));

    public ComponentLookup<T1, T2, T3, T4, T5> ComponentLookup<T1, T2, T3, T4, T5>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct =>
        new ComponentLookup<T1, T2, T3, T4, T5>(AddLookup(typeof(ComponentLookup<T1, T2, T3, T4, T5>), ComponentType<T1>.Id, ComponentType<T2>.Id,
            ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id));

    public ComponentLookup<T1, T2, T3, T4, T5, T6> ComponentLookup<T1, T2, T3, T4, T5, T6>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct =>
        new ComponentLookup<T1, T2, T3, T4, T5, T6>(AddLookup(typeof(ComponentLookup<T1, T2, T3, T4, T5, T6>), ComponentType<T1>.Id, ComponentType<T2>.Id,
            ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id));

    public ComponentLookup<T1, T2, T3, T4, T5, T6, T7> ComponentLookup<T1, T2, T3, T4, T5, T6, T7>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
        where T7 : struct =>
        new ComponentLookup<T1, T2, T3, T4, T5, T6, T7>(AddLookup(typeof(ComponentLookup<T1, T2, T3, T4, T5, T6, T7>), ComponentType<T1>.Id,
            ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id, ComponentType<T7>.Id));

    public ComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8> ComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
        where T7 : struct
        where T8 : struct =>
        new ComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8>(AddLookup(typeof(ComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8>), ComponentType<T1>.Id,
            ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id, ComponentType<T7>.Id,
            ComponentType<T8>.Id));

    public ReadOnlyComponentLookup<T> ReadOnlyComponentLookup<T>() where T : struct =>
        new ReadOnlyComponentLookup<T>(AddLookup(typeof(ReadOnlyComponentLookup<T>), ComponentType<T>.Id));

    public ReadOnlyComponentLookup<T1, T2> ReadOnlyComponentLookup<T1, T2>() where T1 : struct where T2 : struct =>
        new ReadOnlyComponentLookup<T1, T2>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2>), ComponentType<T1>.Id, ComponentType<T2>.Id));

    public ReadOnlyComponentLookup<T1, T2, T3> ReadOnlyComponentLookup<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct =>
        new ReadOnlyComponentLookup<T1, T2, T3>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2, T3>), ComponentType<T1>.Id, ComponentType<T2>.Id,
            ComponentType<T3>.Id));

    public ReadOnlyComponentLookup<T1, T2, T3, T4> ReadOnlyComponentLookup<T1, T2, T3, T4>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct =>
        new ReadOnlyComponentLookup<T1, T2, T3, T4>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2, T3, T4>), ComponentType<T1>.Id, ComponentType<T2>.Id,
            ComponentType<T3>.Id, ComponentType<T4>.Id));

    public ReadOnlyComponentLookup<T1, T2, T3, T4, T5> ReadOnlyComponentLookup<T1, T2, T3, T4, T5>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct =>
        new ReadOnlyComponentLookup<T1, T2, T3, T4, T5>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2, T3, T4, T5>), ComponentType<T1>.Id,
            ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id));

    public ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6> ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct =>
        new ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6>), ComponentType<T1>.Id,
            ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id));

    public ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7> ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
        where T7 : struct =>
        new ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7>), ComponentType<T1>.Id,
            ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id, ComponentType<T7>.Id));

    public ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8> ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8>()
        where T1 : struct
        where T2 : struct
        where T3 : struct
        where T4 : struct
        where T5 : struct
        where T6 : struct
        where T7 : struct
        where T8 : struct =>
        new ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8>(AddLookup(typeof(ReadOnlyComponentLookup<T1, T2, T3, T4, T5, T6, T7, T8>),
            ComponentType<T1>.Id, ComponentType<T2>.Id, ComponentType<T3>.Id, ComponentType<T4>.Id, ComponentType<T5>.Id, ComponentType<T6>.Id,
            ComponentType<T7>.Id, ComponentType<T8>.Id));

    public EntityDestroyer EntityDestroyer()
    {
        ThrowIfClosed();
        Contract.Destroys = true;
        return new EntityDestroyer(Commands);
    }

    public TSpawner Spawner<TSpawner>() where TSpawner : Spawner, new()
    {
        ThrowIfClosed();
        TSpawner spawner = new TSpawner();
        SpawnerLayout layout = _world.GetSpawnerLayout(spawner);
        AddAll(Contract.Changed, layout.Archetype.Components);
        spawner.Initialize(Commands, layout);
        return spawner;
    }

    internal void Close() => _closed = true;

    // Records the components a lookup reads as named by the system, and returns the buffer that gives the lookup its world during Update.
    // The ids are in the order of the lookup's type arguments, which name the repeated component when the generator did not check them.
    private CommandBuffer AddLookup(Type lookup, params ReadOnlySpan<int> componentIds)
    {
        ThrowIfClosed();
        for (int i = 0; i < componentIds.Length; i++)
        {
            if (componentIds[..i].Contains(componentIds[i]))
            {
                Type[] arguments = lookup.GetGenericArguments();
                string name = $"{lookup.Name[..lookup.Name.IndexOf('`')]}<{string.Join(", ", arguments.Select(argument => argument.Name))}>";
                throw new InvalidOperationException($"The lookup {name} names {arguments[i].Name} more than once.");
            }
        }

        foreach (int id in componentIds)
        {
            Contract.Touched.Add(id);
        }

        return Commands;
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new InvalidOperationException("A SystemBuilder creates queries, lookups and writers only while its system is added.");
        }
    }

    private static void AddAll(HashSet<int> target, ComponentSet components)
    {
        foreach (int id in components.Ids)
        {
            target.Add(id);
        }
    }
}
