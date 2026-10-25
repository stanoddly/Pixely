using System.Runtime.CompilerServices;

namespace Pixely.Ecs.Tests;

public class SystemSchedulerTests
{
    internal sealed class Killer : ISystem<Movement, ComponentAdder<Dead>>
    {
        public void Update(Movement movement, ComponentAdder<Dead> dead)
        {
            foreach ((ReadOnlySpan<Entity> entities, _, _) in movement)
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    dead.Add(entities[i], new Dead());
                }
            }
        }
    }

    internal sealed class CorpseCounter : ISystem<Corpses>
    {
        public int Count { get; private set; }

        public void Update(Corpses corpses)
        {
            Count = 0;
            foreach (Columns<ReadOnlySpan<Entity>> columns in corpses)
            {
                Count += columns.Count;
            }
        }
    }

    // Reads the world directly, which a real system does not, to see whether a change has been applied yet. Its query only adds to its contract.
    internal sealed class DeadProbe(World world, Entity entity) : ISystem<Movement>
    {
        public bool SawDead { get; private set; }

        public void Update(Movement movement)
        {
            SawDead = world.Has<Dead>(entity);
        }
    }

    // Hand-written, to throw from Write after writing the blueprints before the one that fails and the one that fails.
    internal struct FailingBlueprint<T> : IBlueprint<FailingBlueprint<T>> where T : struct
    {
        public T Component;
        public bool Fails;

        public static void Describe(BlueprintDescription description) => description.Add<T>();

        public static void Write(ReadOnlySpan<FailingBlueprint<T>> blueprints, BlueprintColumns columns)
        {
            Span<T> components = columns.Get<T>(0);
            for (int i = 0; i < blueprints.Length; i++)
            {
                components[i] = blueprints[i].Component;
                if (blueprints[i].Fails)
                {
                    throw new InvalidOperationException("The blueprint failed.");
                }
            }
        }
    }

    internal struct DuplicateBlueprint : IBlueprint<DuplicateBlueprint>
    {
        public static void Describe(BlueprintDescription description)
        {
            description.Add<Position>();
            description.Add<Position>();
        }

        public static void Write(ReadOnlySpan<DuplicateBlueprint> blueprints, BlueprintColumns columns)
        {
        }
    }

    [Test]
    public void ChangesApplyAtTheEndOfTheUpdate()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new Killer());

        scheduler.Update();

        Assert.That(world.Has<Dead>(entity), Is.True);
    }

    [Test]
    public void LaterSystemThatNamesTheChangedComponentSeesItInTheSameUpdate()
    {
        World world = new World();
        CreateMoving(world);
        CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new Killer());
        CorpseCounter counter = scheduler.Add(new CorpseCounter());

        scheduler.Update();

        Assert.That(counter.Count, Is.EqualTo(2));
    }

    [Test]
    public void LaterSystemThatDoesNotNameTheChangedComponentRunsBeforeTheSyncPoint()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new Killer());
        DeadProbe probe = scheduler.Add(new DeadProbe(world, entity));

        scheduler.Update();

        Assert.That(probe.SawDead, Is.False);
        Assert.That(world.Has<Dead>(entity), Is.True);
    }

    [Test]
    public void EarlierSystemDoesNotSeeChangesOfALaterOneUntilTheNextUpdate()
    {
        World world = new World();
        CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        CorpseCounter counter = scheduler.Add(new CorpseCounter());
        scheduler.Add(new Killer());

        scheduler.Update();
        Assert.That(counter.Count, Is.Zero);

        scheduler.Update();
        Assert.That(counter.Count, Is.EqualTo(1));
    }

    [Test]
    public void SpawnedEntityIsReservedUntilTheSyncPointAndOtherCommandsCanTargetIt()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity spawned = default;
        bool aliveDuringUpdate = true;
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>, ComponentAdder<Name>>((spawner, names) =>
        {
            spawned = spawner.Spawn(new MovingBlueprint { Position = new Position(1, 2), Velocity = new Velocity(3, 4) });
            names.Add(spawned, new Name("spawned"));
            aliveDuringUpdate = world.IsAlive(spawned);
        }));

        scheduler.Update();

        Assert.That(aliveDuringUpdate, Is.False);
        Assert.That(world.Get<Position>(spawned), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Velocity>(spawned), Is.EqualTo(new Velocity(3, 4)));
        Assert.That(world.Get<Name>(spawned), Is.EqualTo(new Name("spawned")));
    }

    [Test]
    public void RunsOfSpawnsBetweenOtherCommandsApplyInRecordingOrder()
    {
        World world = new World();
        Entity existing = world.Create();
        world.Add(existing, new Position());
        SystemScheduler scheduler = new SystemScheduler(world);
        List<Entity> spawned = [];
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>, ComponentAdder<Name>>((spawner, names) =>
        {
            spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(1, 0) }));
            spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(2, 0) }));
            names.Add(spawned[1], new Name("second"));
            names.Add(existing, new Name("existing"));
            spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(3, 0) }));
        }));

        scheduler.Update();

        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(world.Get<Name>(spawned[1]), Is.EqualTo(new Name("second")));
        Assert.That(world.Has<Name>(spawned[0]), Is.False);
        Assert.That(world.Get<Name>(existing), Is.EqualTo(new Name("existing")));
    }

    [Test]
    public void BlueprintWithPositionalPropertiesSpawnsTheirValues()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity spawned = default;
        scheduler.Add(new ActionSystem<Spawner<NamedBlueprint>>(spawner => spawned = spawner.Spawn(new NamedBlueprint(new Name("a"), new Position(5, 6)))));

        scheduler.Update();

        Assert.That(world.Get<Name>(spawned), Is.EqualTo(new Name("a")));
        Assert.That(world.Get<Position>(spawned), Is.EqualTo(new Position(5, 6)));
    }

    [Test]
    public void CommandsOnAnEntityDestroyedEarlierAreSkipped()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new ActionSystem<EntityDestroyer>(destroyer => destroyer.Destroy(entity)));
        scheduler.Add(new ActionSystem<ComponentAdder<Dead>, ComponentRemover<Position>, EntityDestroyer>((dead, positions, destroyer) =>
        {
            dead.Add(entity, new Dead());
            positions.Remove(entity);
            destroyer.Destroy(entity);
        }));

        Assert.DoesNotThrow(scheduler.Update);
        Assert.That(world.IsAlive(entity), Is.False);
    }

    [Test]
    public void AddingAPresentComponentReplacesItsValue()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Name("old"));
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new ActionSystem<ComponentAdder<Name>>(names => names.Add(entity, new Name("new"))));

        scheduler.Update();

        Assert.That(world.Get<Name>(entity), Is.EqualTo(new Name("new")));
    }

    [Test]
    public void DestroyerForcesASyncPointBeforeALaterQuery()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Dead());
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new ActionSystem<EntityDestroyer>(destroyer => destroyer.Destroy(entity)));
        CorpseCounter counter = scheduler.Add(new CorpseCounter());

        scheduler.Update();

        Assert.That(counter.Count, Is.Zero);
    }

    [Test]
    public void RemovedSystemNoLongerRuns()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        int updates = 0;
        ActionSystem system = scheduler.Add(new ActionSystem(() => updates++));

        scheduler.Update();
        scheduler.Remove(system);
        scheduler.Update();

        Assert.That(updates, Is.EqualTo(1));
    }

    [Test]
    public void SystemRemovedDuringUpdateStillRunsInThatUpdate()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        int updates = 0;
        ActionSystem removed = null!;
        scheduler.Add(new ActionSystem(() => scheduler.Remove(removed)));
        removed = scheduler.Add(new ActionSystem(() => updates++));

        scheduler.Update();
        scheduler.Update();

        Assert.That(updates, Is.EqualTo(1));
    }

    [Test]
    public void SystemAddedDuringUpdateFirstRunsInTheNextUpdate()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        int addedUpdates = 0;
        bool added = false;
        scheduler.Add(new ActionSystem(() =>
        {
            if (!added)
            {
                added = true;
                scheduler.Add(new ActionSystem(() => addedUpdates++));
            }
        }));

        scheduler.Update();
        Assert.That(addedUpdates, Is.Zero);

        scheduler.Update();
        Assert.That(addedUpdates, Is.EqualTo(1));
    }

    // 1500 blueprints span two runs of the spawner's lists, so the second run fails after the first one was written. The retry writes into the
    // columns that grew before the failure.
    [TestCase(1)]
    [TestCase(1500)]
    [TestCase(5000)]
    public void FailedBlueprintWriteDropsItsRowsAndReleasesItsEntities(int count)
    {
        World world = new World();
        Entity existing = world.Create();
        world.Add(existing, new Position(7, 7));
        SystemScheduler scheduler = new SystemScheduler(world);
        List<Entity> spawned = [];
        bool fail = true;
        scheduler.Add(new ActionSystem<Spawner<FailingBlueprint<Position>>>(spawner =>
        {
            spawned.Clear();
            for (int i = 0; i < count; i++)
            {
                spawned.Add(spawner.Spawn(new FailingBlueprint<Position> { Component = new Position(i, 0), Fails = fail && i == count - 1 }));
            }
        }));

        Assert.Throws<InvalidOperationException>(scheduler.Update);

        Assert.That(spawned.Where(world.IsAlive), Is.Empty);
        Assert.That(CountPositions(world), Is.EqualTo(1));
        Assert.That(world.Get<Position>(existing), Is.EqualTo(new Position(7, 7)));

        fail = false;
        scheduler.Update();

        Assert.That(spawned.All(world.IsAlive), Is.True);
        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => world.UnsafeGet<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
        Assert.That(CountPositions(world), Is.EqualTo(count + 1));
    }

    [Test]
    public void FailedBlueprintWriteKeepsNoReferenceToTheComponentsItWrote()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        WeakReference written = null!;
        scheduler.Add(new ActionSystem<Spawner<FailingBlueprint<Name>>>(spawner => written = SpawnFailingName(spawner)));

        Assert.Throws<InvalidOperationException>(scheduler.Update);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(written.IsAlive, Is.False);
    }

    [Test]
    public void CommandsRecordedAfterAFailedOneAreDropped()
    {
        World world = new World();
        Entity entity = world.Create();
        SystemScheduler scheduler = new SystemScheduler(world);
        bool fail = true;
        scheduler.Add(new ActionSystem<Spawner<FailingBlueprint<Position>>, ComponentAdder<Name>>((spawner, names) =>
        {
            if (fail)
            {
                spawner.Spawn(new FailingBlueprint<Position> { Fails = true });
                names.Add(entity, new Name("dropped"));
            }
        }));

        Assert.Throws<InvalidOperationException>(scheduler.Update);
        fail = false;
        scheduler.Update();

        Assert.That(world.Has<Name>(entity), Is.False);
    }

    [Test]
    public void CommandsOfLaterSystemsAreDroppedWhenAnEarlierOneFails()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        bool fail = true;
        scheduler.Add(new ActionSystem<Spawner<FailingBlueprint<Position>>>(failing =>
        {
            if (fail)
            {
                failing.Spawn(new FailingBlueprint<Position> { Fails = true });
            }
        }));
        List<Entity> recorded = [];
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>>(later => recorded.Add(later.Spawn(new MovingBlueprint()))));

        Assert.Throws<InvalidOperationException>(scheduler.Update);
        fail = false;
        scheduler.Update();

        Assert.That(world.IsAlive(recorded[0]), Is.False);
        Assert.That(world.IsAlive(recorded[1]), Is.True);
        Assert.That(CountPositions(world), Is.EqualTo(1));
    }

    [Test]
    public void BlueprintDescribingAComponentTwiceThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(new ActionSystem<Spawner<DuplicateBlueprint>>(_ => { })));
    }

    [Test]
    public void SpawnerForcesASyncPointBeforeALaterQueryOfItsComponents()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        int count = -1;
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>>(spawner => spawner.Spawn(new MovingBlueprint())));
        scheduler.Add(new ActionSystem<Movement>(movement =>
        {
            count = 0;
            foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> columns in movement)
            {
                count += columns.Count;
            }
        }));

        scheduler.Update();

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public void AddingAComponentALaterQueryExcludesForcesASyncPointBeforeIt()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position());
        SystemScheduler scheduler = new SystemScheduler(world);
        int visible = -1;
        scheduler.Add(new ActionSystem<ComponentAdder<Frozen>>(frozen => frozen.Add(entity, new Frozen())));
        scheduler.Add(new ActionSystem<UnfrozenPositions>(positions =>
        {
            visible = 0;
            foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>> columns in positions)
            {
                visible += columns.Count;
            }
        }));

        scheduler.Update();

        Assert.That(visible, Is.Zero);
    }

    [Test]
    public void SpanWritesAreVisibleToLaterSystemsWithoutASyncPoint()
    {
        World world = new World();
        CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        Position seen = default;
        scheduler.Add(new ActionSystem<Movement>(movement =>
        {
            foreach ((_, Span<Position> positions, _) in movement)
            {
                positions[0] = new Position(7, 8);
            }
        }));
        scheduler.Add(new ActionSystem<Positions>(positions =>
        {
            foreach (Columns<ReadOnlySpan<Position>> columns in positions)
            {
                seen = columns.Item1[0];
            }
        }));

        scheduler.Update();

        Assert.That(seen, Is.EqualTo(new Position(7, 8)));
    }

    [Test]
    public void RemoverRemovesAPresentComponentAndSkipsAnAbsentOne()
    {
        World world = new World();
        Entity present = world.Create();
        world.Add(present, new Position());
        Entity absent = world.Create();
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new ActionSystem<ComponentRemover<Position>>(remover =>
        {
            remover.Remove(present);
            remover.Remove(absent);
        }));

        Assert.DoesNotThrow(scheduler.Update);
        Assert.That(world.Has<Position>(present), Is.False);
        Assert.That(world.Has<Position>(absent), Is.False);
    }

    [Test]
    public void AddsAndRemovesOfOneComponentApplyInRecordingOrder()
    {
        World world = new World();
        Entity removedLast = world.Create();
        Entity addedLast = world.Create();
        world.Add(addedLast, new Dead());
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new ActionSystem<ComponentAdder<Dead>, ComponentRemover<Dead>>((adder, remover) =>
        {
            adder.Add(removedLast, new Dead());
            remover.Remove(removedLast);
            remover.Remove(addedLast);
            adder.Add(addedLast, new Dead());
        }));

        scheduler.Update();

        Assert.That(world.Has<Dead>(removedLast), Is.False);
        Assert.That(world.Has<Dead>(addedLast), Is.True);
    }

    [Test]
    public void SystemsRunInTheOrderTheyWereAdded()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        List<int> order = [];
        scheduler.Add(new ActionSystem(() => order.Add(1)));
        scheduler.Add(new ActionSystem(() => order.Add(2)));
        scheduler.Add(new ActionSystem(() => order.Add(3)));

        scheduler.Update();

        Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void BuilderRefusesUseAfterItsSystemIsCreated()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        SystemBuilder builder = null!;
        scheduler.Add(new ActionSystem(() => { }), created =>
        {
            builder = created;
            return static system => system.Update();
        }, SystemScheduler.ReserveOrder());

        Assert.Throws<InvalidOperationException>(() => builder.ComponentAdder<Dead>());
    }

    // The commands recorded since the last sync point are dropped, so the next successful update does not apply them, and the spawn's entity is released.
    [Test]
    public void SystemThatThrowsDropsTheCommandsSinceTheLastSyncPoint()
    {
        World world = new World();
        Entity entity = world.Create();
        SystemScheduler scheduler = new SystemScheduler(world);
        bool fail = true;
        Entity spawned = default;
        scheduler.Add(new ActionSystem<ComponentAdder<Name>>(names =>
        {
            if (fail)
            {
                names.Add(entity, new Name("dropped"));
            }
        }));
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>>(spawner =>
        {
            if (fail)
            {
                spawned = spawner.Spawn(new MovingBlueprint());
                throw new InvalidOperationException("The system failed.");
            }
        }));

        Assert.Throws<InvalidOperationException>(scheduler.Update);
        fail = false;
        scheduler.Update();

        Assert.That(world.Has<Name>(entity), Is.False);
        Assert.That(world.IsAlive(spawned), Is.False);
        Assert.That(CountPositions(world), Is.Zero);

        // A released reservation frees its index, so the next entity reuses it with a newer version. ToString shows the index, which is internal.
        Entity next = world.Create();
        string spawnedIndex = spawned.ToString()[..spawned.ToString().IndexOf(',')];
        Assert.That(next.ToString(), Does.StartWith(spawnedIndex));
        Assert.That(next, Is.Not.EqualTo(spawned));
    }

    // Changes applied at a sync point before the failing system stay, while the commands the failing system recorded after it are dropped.
    [Test]
    public void SystemThatThrowsKeepsTheChangesOfEarlierSyncPoints()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        bool fail = true;
        Entity spawned = default;
        ComponentAdder<Name> keptNames = null!;
        scheduler.Add(new Killer());
        scheduler.Add(new ActionSystem<Corpses, ComponentAdder<Name>, Spawner<MovingBlueprint>>((_, names, spawner) =>
        {
            keptNames = names;
            if (fail)
            {
                names.Add(entity, new Name("dropped"));
                spawned = spawner.Spawn(new MovingBlueprint());
                throw new InvalidOperationException("The system failed.");
            }
        }));

        Assert.Throws<InvalidOperationException>(scheduler.Update);

        Assert.That(world.Has<Dead>(entity), Is.True);
        Assert.Throws<InvalidOperationException>(() => keptNames.Add(entity, new Name("kept")));

        fail = false;
        scheduler.Update();

        Assert.That(world.Has<Name>(entity), Is.False);
        Assert.That(world.IsAlive(spawned), Is.False);
    }

    [Test]
    public void WriterKeptPastUpdateThrows()
    {
        World world = new World();
        Entity entity = world.Create();
        SystemScheduler scheduler = new SystemScheduler(world);
        ComponentAdder<Dead> keptAdder = null!;
        Spawner<MovingBlueprint> keptSpawner = null!;
        scheduler.Add(new ActionSystem<ComponentAdder<Dead>, Spawner<MovingBlueprint>>((dead, spawner) =>
        {
            keptAdder = dead;
            keptSpawner = spawner;
        }));
        scheduler.Update();

        Assert.Throws<InvalidOperationException>(() => keptAdder.Add(entity, new Dead()));
        Assert.Throws<InvalidOperationException>(() => keptSpawner.Spawn(new MovingBlueprint()));
        scheduler.Update();
        Assert.That(world.Has<Dead>(entity), Is.False);
    }

    // Creates the component's string here, so nothing but the world and the spawner can reference it once this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SpawnFailingName(Spawner<FailingBlueprint<Name>> spawner)
    {
        string value = new string('x', 8);
        spawner.Spawn(new FailingBlueprint<Name> { Component = new Name(value), Fails = true });
        return new WeakReference(value);
    }

    private static int CountPositions(World world)
    {
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>> columns in world.Query<UnfrozenPositions>())
        {
            count += columns.Count;
        }

        return count;
    }

    private static Entity CreateMoving(World world)
    {
        Entity entity = world.Create();
        world.Add(entity, new Position());
        world.Add(entity, new Velocity());
        return entity;
    }
}
