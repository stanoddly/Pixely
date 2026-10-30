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
        AddAll(Contract.Touched, query.State.Optional);
        AddAll(Contract.Touched, query.State.Excluded);
        return query;
    }

    // A relationship destroyed with its target destroys its source when the target is not alive, so adding one counts as destroying.
    public ComponentAdder<T> ComponentAdder<T>() where T : struct
    {
        ThrowIfClosed();
        Contract.Changed.Add(ComponentType<T>.Id);
        Contract.Destroys |= default(T) is IDestroyedWithTarget;
        return new ComponentAdder<T>(Commands);
    }

    public ComponentRemover<T> ComponentRemover<T>() where T : struct
    {
        ThrowIfClosed();
        Contract.Changed.Add(ComponentType<T>.Id);
        return new ComponentRemover<T>(Commands);
    }

    public ComponentLookup<T> ComponentLookup<T>() where T : struct =>
        new ComponentLookup<T>(AddWritableLookup(typeof(ComponentLookup<T>), ComponentType<T>.Id));

    public ReadOnlyComponentLookup<T> ReadOnlyComponentLookup<T>() where T : struct =>
        new ReadOnlyComponentLookup<T>(AddLookup(typeof(ReadOnlyComponentLookup<T>), ComponentType<T>.Id));

    public RelationshipSources<T> RelationshipSources<T>() where T : struct, IRelationship =>
        new RelationshipSources<T>(AddLookup(typeof(RelationshipSources<T>), ComponentType<T>.Id));

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
        Contract.Destroys |= layout.Archetype.Relationships.Any(relationship => relationship.Type.DestroyedWithTarget);
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
                throw new InvalidOperationException($"The lookup {FormatLookup(lookup)} names {lookup.GetGenericArguments()[i].Name} more than once.");
            }
        }

        foreach (int id in componentIds)
        {
            Contract.Touched.Add(id);
        }

        return Commands;
    }

    // A relationship changes only at sync points, where the world updates its index, so no lookup writes one. The generator registers relationships when
    // their assembly loads, which is before any of their ids exists.
    private CommandBuffer AddWritableLookup(Type lookup, params ReadOnlySpan<int> componentIds)
    {
        foreach (int id in componentIds)
        {
            if (RelationshipType.Find(id) is { } relationship)
            {
                throw new InvalidOperationException(
                    $"The lookup {FormatLookup(lookup)} writes {relationship.Name}, a relationship, which changes only through ComponentAdder<{relationship.Name}>, ComponentRemover<{relationship.Name}> and spawners. Use ReadOnlyComponentLookup<...>.");
            }
        }

        return AddLookup(lookup, componentIds);
    }

    private static string FormatLookup(Type lookup) => $"{lookup.Name[..lookup.Name.IndexOf('`')]}<{string.Join(", ", lookup.GetGenericArguments().Select(argument => argument.Name))}>";

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
