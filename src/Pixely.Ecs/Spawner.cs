namespace Pixely.Ecs;

internal sealed class SpawnerIds : StaticTypeIdMap<SpawnerIds>;

internal sealed class SpawnerId<TSpawner> : StaticTypeIdMap<SpawnerIds, TSpawner>;

// The archetype a spawner type spawns into, and the column of each of its type arguments.
internal sealed class SpawnerLayout
{
    public SpawnerLayout(Archetype archetype, int[] columns)
    {
        Archetype = archetype;
        Columns = columns;
    }

    public Archetype Archetype { get; }

    public int[] Columns { get; }
}

// The components of the entities a spawner spawns: its type arguments, whose values Spawn takes, then the tags of its IWith<...>.
internal sealed class SpawnerDescription : IFilterDescription
{
    private readonly string _subject;
    private readonly List<int> _ids = [];
    private readonly List<ColumnType> _types = [];

    public SpawnerDescription(string subject)
    {
        _subject = subject;
    }

    public IReadOnlyList<int> Ids => _ids;

    public IReadOnlyList<ColumnType> Types => _types;

    // The type arguments come first, so the column of type argument i is the column of Ids[i].
    public int ArgumentCount { get; private set; }

    public void Argument<T>() where T : struct
    {
        if (typeof(T) == typeof(Entity))
        {
            throw new InvalidOperationException($"{_subject} takes Entity, which is not a component. Spawn returns the entity.");
        }

        if (ColumnType<T>.Instance.IsTag)
        {
            throw new InvalidOperationException($"{_subject} takes {ComponentType<T>.Name}, a tag, which has no data. Use IWith<{ComponentType<T>.Name}> instead.");
        }

        Add<T>();
        ArgumentCount++;
    }

    public void With<T>() where T : struct
    {
        if (!ColumnType<T>.Instance.IsTag)
        {
            throw new InvalidOperationException($"{_subject} names {ComponentType<T>.Name} in IWith<...>, but it has data. Name it in Spawner<...>, whose Spawn takes its value.");
        }

        Add<T>();
    }

    public void Without<T>() where T : struct => throw new InvalidOperationException($"{_subject} implements IWithout<...>, which a spawner has no use for.");

    private void Add<T>() where T : struct
    {
        int id = ComponentType<T>.Id;
        if (_ids.Contains(id))
        {
            throw new InvalidOperationException($"{_subject} names {ComponentType<T>.Name} more than once.");
        }

        _ids.Add(id);
        _types.Add(ColumnType<T>.Instance);
    }
}

// Creates entities with the components of its type arguments and the tags of its IWith<...> at the next sync point, directly in their archetype.
// A system takes a spawner in its constructor, and the SystemBuilder creates one for the system, so a spawner class needs a public parameterless constructor.
public abstract class Spawner : IRecordedCommands, IRoomRequest
{
    private readonly ChunkedList<Entity> _entities = new ChunkedList<Entity>();
    private CommandBuffer? _commands;
    private SpawnerLayout? _layout;
    private int _room;

    private protected Spawner()
    {
    }

    internal SpawnerLayout Layout => _layout!;

    private CommandBuffer Commands => _commands ?? throw new InvalidOperationException($"The spawner {GetType().Name} was created with new. Get it through a system's constructor.");

    internal SpawnerDescription Describe()
    {
        SpawnerDescription description = new SpawnerDescription($"The spawner {GetType().Name}");
        DescribeArguments(description);
        if (this is IWith with)
        {
            with.DescribeWith(description);
        }

        if (this is IWithout without)
        {
            without.DescribeWithout(description);
        }

        return description;
    }

    internal void Initialize(CommandBuffer commands, SpawnerLayout layout)
    {
        _commands = commands;
        _layout = layout;
        commands.AddSource(this);
    }

    // Grows the archetype at the sync point, before any of the system's commands, so it never replaces the arrays under a running query.
    // The buffer sums the requests of the system for each archetype, so spawners that share one each get room for their own spawns. The room
    // is rows, not spawns: entities the system's other writers move into the archetype fill it too. A system that throws drops the request with
    // its other commands.
    public void EnsureRoomFor(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        CommandBuffer commands = Commands;
        commands.ThrowIfNotRecording();
        if (count == 0)
        {
            return;
        }

        if (_room == 0)
        {
            commands.RequestRoom(this);
        }

        _room = checked(_room + count);
    }

    // The entity is reserved now and becomes alive at the sync point, so other commands of the same system can already target it.
    // Spawn of the derived class then adds the components at the index of the entity.
    private protected Entity RecordSpawn()
    {
        CommandBuffer commands = Commands;
        commands.ThrowIfNotRecording();
        Entity entity = commands.World.Reserve();
        commands.Record(this, _entities.Add(entity));
        return entity;
    }

    private protected abstract void DescribeArguments(SpawnerDescription description);

    // Copies the components of the spawns from first on into the rows from firstRow on.
    private protected abstract void WriteComponents(int first, int firstRow, int count);

    private protected abstract void ClearComponents();

    private protected Span<T> GetRows<T>(int argument, int firstRow, int count) where T : struct =>
        _layout!.Archetype.GetColumn<T>(_layout.Columns[argument]).GetRows(firstRow, count);

    // A ChunkedList keeps its items in chunks, so they are copied run by run.
    private protected static void CopyRuns<T>(ChunkedList<T> items, int first, Span<T> rows)
    {
        for (int copied = 0; copied < rows.Length;)
        {
            ReadOnlySpan<T> run = items.GetRun(first + copied, rows.Length - copied);
            run.CopyTo(rows[copied..]);
            copied += run.Length;
        }
    }

    void IRecordedCommands.Apply(World world, int first, int count)
    {
        int firstRow = world.AddSpawnedRows(_layout!.Archetype, count);
        CopyRuns(_entities, first, _layout.Archetype.GetEntities(firstRow, count));
        WriteComponents(first, firstRow, count);
        world.SetSpawnedRecords(_layout.Archetype, firstRow, count);
    }

    void IRecordedCommands.Discard(World world, int index) => world.ReleaseReservation(_entities[index]);

    void IRecordedCommands.Clear()
    {
        _entities.Clear();
        ClearComponents();
        _room = 0;
    }

    int IRoomRequest.Room => _room;

    Archetype IRoomRequest.Archetype => _layout!.Archetype;
}
