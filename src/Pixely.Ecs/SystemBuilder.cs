namespace Pixely.Ecs;

// What a system's ISystem<...> declares: the components its queries name and the structural changes its writers make.
internal sealed class SystemContract
{
    public HashSet<int> Touched { get; } = [];

    public HashSet<int> Changed { get; } = [];

    public bool Destroys { get; set; }
}

// Creates the queries and writers of one system while it is added to a SystemScheduler, and records its contract.
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

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw new InvalidOperationException("A SystemBuilder creates queries and writers only while its system is added.");
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
