using System.Runtime.CompilerServices;

namespace Pixely.Ecs.Tests;

public class SystemSchedulerTests
{
    internal sealed class Killer(Movement movement, ComponentAdder<Dead> dead) : ISystem
    {
        public void Update()
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

    internal sealed class CorpseCounter(Corpses corpses) : ISystem
    {
        public int Count { get; private set; }

        public void Update()
        {
            Count = 0;
            foreach (Columns<ReadOnlySpan<Entity>> columns in corpses)
            {
                Count += columns.Count;
            }
        }
    }

    // Reads the world directly, which a real system does not, to see whether a change has been applied yet. Its query only adds to its contract.
    internal sealed class DeadProbe : ISystem
    {
        private readonly World _world;
        private readonly Entity _entity;

        public DeadProbe(World world, Entity entity, Movement movement)
        {
            _world = world;
            _entity = entity;
        }

        public bool SawDead { get; private set; }

        public void Update()
        {
            SawDead = _world.Has<Dead>(_entity);
        }
    }

    // Wrong on purpose: a spawner has no use for IWithout<...>. No system names it, so only the world checks it.
    public sealed class ExcludingSpawner : Spawner<Position>, IWithout<Frozen>;

    [Test]
    public void ChangesApplyAtTheEndOfTheUpdate()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

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
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        CorpseCounter counter = scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));

        scheduler.Update();

        Assert.That(counter.Count, Is.EqualTo(2));
    }

    [Test]
    public void LaterSystemThatDoesNotNameTheChangedComponentRunsBeforeTheSyncPoint()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        DeadProbe probe = scheduler.Add(builder => new DeadProbe(world, entity, builder.Query<Movement>()));

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
        CorpseCounter counter = scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

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
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ComponentAdder<Name>(), (spawner, names) =>
        {
            spawned = spawner.Spawn(new Position(1, 2), new Velocity(3, 4));
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
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ComponentAdder<Name>(), (spawner, names) =>
        {
            spawned.Add(spawner.Spawn(new Position(1, 0), default));
            spawned.Add(spawner.Spawn(new Position(2, 0), default));
            names.Add(spawned[1], new Name("second"));
            names.Add(existing, new Name("existing"));
            spawned.Add(spawner.Spawn(new Position(3, 0), default));
        }));

        scheduler.Update();

        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(world.Get<Name>(spawned[1]), Is.EqualTo(new Name("second")));
        Assert.That(world.Has<Name>(spawned[0]), Is.False);
        Assert.That(world.Get<Name>(existing), Is.EqualTo(new Name("existing")));
    }

    // Each type argument goes into the column of its component, whatever the order of the archetype's columns.
    [Test]
    public void SpawnerWritesEachTypeArgumentIntoItsComponent()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity spawned = default;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Name, Position, Velocity>>(), spawner => spawned = spawner.Spawn(new Name("a"), new Position(5, 6), new Velocity(7, 8))));

        scheduler.Update();

        Assert.That(world.Get<Name>(spawned), Is.EqualTo(new Name("a")));
        Assert.That(world.Get<Position>(spawned), Is.EqualTo(new Position(5, 6)));
        Assert.That(world.Get<Velocity>(spawned), Is.EqualTo(new Velocity(7, 8)));
    }

    [Test]
    public void CommandsOnAnEntityDestroyedEarlierAreSkipped()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => ActionSystem.Of(builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity)));
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Dead>(), builder.ComponentRemover<Position>(), builder.EntityDestroyer(), (dead, positions, destroyer) =>
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
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Name>(), names => names.Add(entity, new Name("new"))));

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
        scheduler.Add(builder => ActionSystem.Of(builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity)));
        CorpseCounter counter = scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));

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

    // 5000 spawns span three runs of the spawner's lists, so the retry copies them run by run.
    [TestCase(1)]
    [TestCase(5000)]
    public void FailedSpawnLeavesNoRowsAndReleasesItsEntities(int count)
    {
        World world = new World();
        Entity existing = world.Create();
        world.Add(existing, new Position(7, 7));
        SystemScheduler scheduler = new SystemScheduler(world);
        List<Entity> spawned = [];
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            spawned.Clear();
            for (int i = 0; i < count; i++)
            {
                spawned.Add(spawner.Spawn(new Position(i, 0), new Velocity(0, i)));
            }
        }));

        UpdateWhileAQueryRuns(world, scheduler);

        Assert.That(spawned.Where(world.IsAlive), Is.Empty);
        Assert.That(CountPositions(world), Is.EqualTo(1));
        Assert.That(world.Get<Position>(existing), Is.EqualTo(new Position(7, 7)));

        scheduler.Update();

        Assert.That(spawned.All(world.IsAlive), Is.True);
        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => world.UnsafeGet<Velocity>(entity).Y), Is.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
        Assert.That(CountPositions(world), Is.EqualTo(count + 1));
    }

    [Test]
    public void FailedSpawnKeepsNoReferenceToItsComponents()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        WeakReference spawned = null!;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Name>>(), spawner => spawned = SpawnName(spawner)));

        UpdateWhileAQueryRuns(world, scheduler);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(spawned.IsAlive, Is.False);
    }

    [Test]
    public void CommandsRecordedAfterAFailedOneAreDropped()
    {
        World world = new World();
        Entity entity = world.Create();
        SystemScheduler scheduler = new SystemScheduler(world);
        bool record = true;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ComponentAdder<Name>(), (spawner, names) =>
        {
            if (record)
            {
                spawner.Spawn(default, default);
                names.Add(entity, new Name("dropped"));
            }
        }));

        UpdateWhileAQueryRuns(world, scheduler);
        record = false;
        scheduler.Update();

        Assert.That(world.Has<Name>(entity), Is.False);
    }

    [Test]
    public void CommandsOfLaterSystemsAreDroppedWhenAnEarlierOneFails()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<FrozenSpawner>(), earlier => earlier.Spawn(default)));
        List<Entity> recorded = [];
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), later => recorded.Add(later.Spawn(default, default))));

        UpdateWhileAQueryRuns(world, scheduler);
        scheduler.Update();

        Assert.That(world.IsAlive(recorded[0]), Is.False);
        Assert.That(world.IsAlive(recorded[1]), Is.True);
        Assert.That(CountPositions(world), Is.EqualTo(1));
    }

    [Test]
    public void SpawnerNamingAComponentTwiceTakingEntityOrExcludingThrows()
    {
        World world = new World();

        Assert.Throws<InvalidOperationException>(() => world.EnsureCapacity<Spawner<Position, Position>>(1));
        Assert.Throws<InvalidOperationException>(() => world.EnsureCapacity<Spawner<Entity>>(1));
        Assert.Throws<InvalidOperationException>(() => world.EnsureCapacity<ExcludingSpawner>(1));
    }

    [Test]
    public void SpawnerForcesASyncPointBeforeALaterQueryOfItsComponents()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        int count = -1;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner => spawner.Spawn(default, default)));
        scheduler.Add(builder => ActionSystem.Of(builder.Query<Movement>(), movement =>
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
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Frozen>(), frozen => frozen.Add(entity, new Frozen())));
        scheduler.Add(builder => ActionSystem.Of(builder.Query<UnfrozenPositions>(), positions =>
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
        scheduler.Add(builder => ActionSystem.Of(builder.Query<Movement>(), movement =>
        {
            foreach ((_, Span<Position> positions, _) in movement)
            {
                positions[0] = new Position(7, 8);
            }
        }));
        scheduler.Add(builder => ActionSystem.Of(builder.Query<Positions>(), positions =>
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
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentRemover<Position>(), remover =>
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
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Dead>(), builder.ComponentRemover<Dead>(), (adder, remover) =>
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
        scheduler.Add(created =>
        {
            builder = created;
            return new ActionSystem(() => { });
        });

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
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Name>(), names =>
        {
            if (fail)
            {
                names.Add(entity, new Name("dropped"));
            }
        }));
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            if (fail)
            {
                spawned = spawner.Spawn(default, default);
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
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        scheduler.Add(builder => ActionSystem.Of(builder.Query<Corpses>(), builder.ComponentAdder<Name>(), builder.Spawner<MovingSpawner>(), (_, names, spawner) =>
        {
            keptNames = names;
            if (fail)
            {
                names.Add(entity, new Name("dropped"));
                spawned = spawner.Spawn(default, default);
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
        MovingSpawner keptSpawner = null!;
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Dead>(), builder.Spawner<MovingSpawner>(), (dead, spawner) =>
        {
            keptAdder = dead;
            keptSpawner = spawner;
        }));
        scheduler.Update();

        Assert.Throws<InvalidOperationException>(() => keptAdder.Add(entity, new Dead()));
        Assert.Throws<InvalidOperationException>(() => keptSpawner.Spawn(default, default));
        scheduler.Update();
        Assert.That(world.Has<Dead>(entity), Is.False);
    }

    // A system gets its writers and lookups in its constructor, but they work only during its Update.
    [Test]
    public void WriterOrLookupUsedWhileItsSystemIsCreatedThrows()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder =>
        {
            builder.ComponentAdder<Dead>().Add(entity, new Dead());
            return new ActionSystem(() => { });
        }));
        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder =>
        {
            builder.Spawner<MovingSpawner>().Spawn(default, default);
            return new ActionSystem(() => { });
        }));
        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder =>
        {
            _ = builder.ReadOnlyComponentLookup<Position>().Has(entity);
            return new ActionSystem(() => { });
        }));
        scheduler.Update();

        Assert.That(world.Has<Dead>(entity), Is.False);
        Assert.That(CountPositions(world), Is.EqualTo(1));
    }

    [Test]
    public void LookupReadsAndWritesTheComponentsOfAGivenEntity()
    {
        World world = new World();
        Entity moving = CreateMoving(world);
        world.Get<Velocity>(moving) = new Velocity(1, 2);
        Entity still = world.Create();
        world.Add(still, new Position(5, 5));
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position>(), builder.ReadOnlyComponentLookup<Velocity>(), (positions, velocities) =>
        {
            foreach (Entity entity in (Entity[])[moving, still])
            {
                if (velocities.Has(entity))
                {
                    ref Position position = ref positions[entity];
                    position = new Position(position.X + velocities[entity].X, position.Y + velocities[entity].Y);
                }
            }
        }));

        scheduler.Update();

        Assert.That(world.Get<Position>(moving), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Position>(still), Is.EqualTo(new Position(5, 5)));
    }

    [Test]
    public void LookupSeesTheChangesOfAnEarlierSystemInTheSameUpdate()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        bool sawDead = false;
        scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<Dead>(), dead => sawDead = dead.Has(entity)));

        scheduler.Update();

        Assert.That(sawDead, Is.True);
    }

    [Test]
    public void LookupHasIsFalseForADestroyedEntity()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        world.Destroy(entity);
        SystemScheduler scheduler = new SystemScheduler(world);
        bool has = true;
        scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<Position>(), positions => has = positions.Has(entity)));

        scheduler.Update();

        Assert.That(has, Is.False);
    }

    [Test]
    public void LookupOfSeveralComponentsReadsAndWritesEachOfThem()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        world.Get<Velocity>(entity) = new Velocity(1, 2);
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position, Velocity>(), lookup =>
        {
            ComponentRefs<Position, Velocity> components = lookup[entity];
            components.Item1 = new Position(components.Item1.X + components.Item2.X, components.Item1.Y + components.Item2.Y);
            components.Item2 = default;
        }));

        scheduler.Update();

        Assert.That(world.Get<Position>(entity), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Velocity>(entity), Is.EqualTo(default(Velocity)));
    }

    [Test]
    public void LookupOfSeveralComponentsHasOnlyAnAliveEntityWithAllOfThem()
    {
        World world = new World();
        Entity frozen = CreateMoving(world);
        world.Add(frozen, new Frozen());
        Entity moving = CreateMoving(world);
        Entity destroyed = CreateMoving(world);
        world.Add(destroyed, new Frozen());
        world.Destroy(destroyed);
        SystemScheduler scheduler = new SystemScheduler(world);
        bool[] has = [];
        scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<Position, Frozen>(), lookup =>
        {
            has = [lookup.Has(frozen), lookup.Has(moving), lookup.Has(destroyed)];
            Assert.Throws<InvalidOperationException>(() => { _ = lookup[moving]; });
            Assert.Throws<ArgumentException>(() => { _ = lookup[destroyed]; });
        }));

        scheduler.Update();

        Assert.That(has, Is.EqualTo(new[] { true, false, false }));
    }

    // The generator reports the lookup of a system added with AddSystem, so the builder sees one only from code that calls it directly.
    [Test]
    public void LookupNamingAComponentTwiceThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder =>
        {
            builder.ReadOnlyComponentLookup<Position, Velocity, Position>();
            return new ActionSystem(() => { });
        }));

        Assert.That(exception.Message, Is.EqualTo("The lookup ReadOnlyComponentLookup<Position, Velocity, Position> names Position more than once."));
    }

    [Test]
    public void LookupKeptPastUpdateThrows()
    {
        World world = new World();
        Entity entity = CreateMoving(world);
        SystemScheduler scheduler = new SystemScheduler(world);
        ComponentLookup<Position> keptPositions = null!;
        ReadOnlyComponentLookup<Velocity> keptVelocities = null!;
        ReadOnlyComponentLookup<Position, Velocity> keptBoth = null!;
        scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position>(), builder.ReadOnlyComponentLookup<Velocity>(), builder.ReadOnlyComponentLookup<Position, Velocity>(), (positions, velocities, both) =>
        {
            keptPositions = positions;
            keptVelocities = velocities;
            keptBoth = both;
        }));
        scheduler.Update();

        Assert.Throws<InvalidOperationException>(() => keptPositions.Has(entity));
        Assert.Throws<InvalidOperationException>(() => _ = keptPositions[entity]);
        Assert.Throws<InvalidOperationException>(() => keptVelocities.Has(entity));
        Assert.Throws<InvalidOperationException>(() => _ = keptVelocities[entity]);
        Assert.Throws<InvalidOperationException>(() => keptBoth.Has(entity));
        Assert.Throws<InvalidOperationException>(() => { _ = keptBoth[entity]; });
    }

    // Creates the component's string here, so nothing but the world and the spawner can reference it once this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference SpawnName(Spawner<Name> spawner)
    {
        string value = new string('x', 8);
        spawner.Spawn(new Name(value));
        return new WeakReference(value);
    }

    // Copying a spawn's components cannot fail, so the tests fail a sync point by running it inside a query, where its first command throws.
    private static void UpdateWhileAQueryRuns(World world, SystemScheduler scheduler)
    {
        Entity running = world.Create();
        world.Add(running, new Mass(1));
        foreach (Columns<ReadOnlySpan<Mass>> _ in world.Query<Masses>())
        {
            Assert.Throws<InvalidOperationException>(scheduler.Update);
        }

        world.Destroy(running);
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
