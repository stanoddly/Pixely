namespace Pixely.Ecs;

// What a system's constructor declares: the components its queries and lookups name and the structural changes its writers make.
internal sealed class SystemContract
{
    public HashSet<int> Touched { get; } = [];

    public HashSet<int> Changed { get; } = [];

    public bool Destroys { get; set; }
}

// Creates the queries, lookups and writers of one system while it is added to a SystemScheduler, and records its contract.
public sealed partial class SystemBuilder
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

    public ReadOnlyComponentLookup<T> ReadOnlyComponentLookup<T>() where T : struct =>
        new ReadOnlyComponentLookup<T>(AddLookup(typeof(ReadOnlyComponentLookup<T>), ComponentType<T>.Id));

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
