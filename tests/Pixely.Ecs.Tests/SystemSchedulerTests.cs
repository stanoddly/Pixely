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

    // Reads through the lookup of code outside systems, which a real system does not, so its contract does not name Dead and it can see whether a
    // change has been applied yet. Its query only adds to its contract.
    internal sealed class DeadProbe : ISystem
    {
        private readonly ReadOnlyComponentLookup<Dead> _dead;
        private readonly Entity _entity;

        public DeadProbe(ReadOnlyComponentLookup<Dead> dead, Entity entity, Movement movement)
        {
            _dead = dead;
            _entity = entity;
        }

        public bool SawDead { get; private set; }

        public void Update()
        {
            SawDead = _dead.Has(_entity);
        }
    }

    // Wrong on purpose: a spawner has no use for IWithout<...>. No system names it, so only the world checks it.
    public sealed class ExcludingSpawner : Spawner<Position>, IWithout<Frozen>;

    [Test]
    public void ChangesApplyAtTheEndOfTheUpdate()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        world.Scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

        world.Scheduler.Update();

        Assert.That(world.Lookup<Dead>().Has(entity), Is.True);
    }

    [Test]
    public void LaterSystemThatNamesTheChangedComponentSeesItInTheSameUpdate()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position(), new Velocity());
        world.Spawn(new Position(), new Velocity());
        world.Scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        CorpseCounter counter = world.Scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));

        world.Scheduler.Update();

        Assert.That(counter.Count, Is.EqualTo(2));
    }

    [Test]
    public void LaterSystemThatDoesNotNameTheChangedComponentRunsBeforeTheSyncPoint()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        world.Scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        DeadProbe probe = world.Scheduler.Add(builder => new DeadProbe(world.Lookup<Dead>(), entity, builder.Query<Movement>()));

        world.Scheduler.Update();

        Assert.That(probe.SawDead, Is.False);
        Assert.That(world.Lookup<Dead>().Has(entity), Is.True);
    }

    [Test]
    public void EarlierSystemDoesNotSeeChangesOfALaterOneUntilTheNextUpdate()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position(), new Velocity());
        CorpseCounter counter = world.Scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));
        world.Scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

        world.Scheduler.Update();
        Assert.That(counter.Count, Is.Zero);

        world.Scheduler.Update();
        Assert.That(counter.Count, Is.EqualTo(1));
    }

    [Test]
    public void SpawnedEntityIsReservedUntilTheSyncPointAndOtherCommandsCanTargetIt()
    {
        using TestWorld world = new TestWorld();
        Entity spawned = default;
        bool aliveDuringUpdate = true;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ComponentAdder<Name>(), builder.ReadOnlyComponentLookup<Position>(), (spawner, names, positions) =>
        {
            spawned = spawner.Spawn(new Position(1, 2), new Velocity(3, 4));
            names.Add(spawned, new Name("spawned"));
            aliveDuringUpdate = positions.Has(spawned);
        }));

        world.Scheduler.Update();

        Assert.That(aliveDuringUpdate, Is.False);
        Assert.That(world.Lookup<Position>()[spawned], Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Lookup<Velocity>()[spawned], Is.EqualTo(new Velocity(3, 4)));
        Assert.That(world.Lookup<Name>()[spawned], Is.EqualTo(new Name("spawned")));
    }

    [Test]
    public void RunsOfSpawnsBetweenOtherCommandsApplyInRecordingOrder()
    {
        using TestWorld world = new TestWorld();
        Entity existing = world.Spawn(new Position());
        List<Entity> spawned = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ComponentAdder<Name>(), (spawner, names) =>
        {
            spawned.Add(spawner.Spawn(new Position(1, 0), default));
            spawned.Add(spawner.Spawn(new Position(2, 0), default));
            names.Add(spawned[1], new Name("second"));
            names.Add(existing, new Name("existing"));
            spawned.Add(spawner.Spawn(new Position(3, 0), default));
        }));

        world.Scheduler.Update();

        ReadOnlyComponentLookup<Name> names = world.Lookup<Name>();
        Assert.That(spawned.Select(entity => world.Lookup<Position>()[entity].X), Is.EqualTo(new[] { 1f, 2f, 3f }));
        Assert.That(names[spawned[1]], Is.EqualTo(new Name("second")));
        Assert.That(names.Has(spawned[0]), Is.False);
        Assert.That(names[existing], Is.EqualTo(new Name("existing")));
    }

    // Each type argument goes into the column of its component, whatever the order of the archetype's columns.
    [Test]
    public void SpawnerWritesEachTypeArgumentIntoItsComponent()
    {
        using TestWorld world = new TestWorld();

        Entity spawned = world.Scheduler.RunOnce(builder => builder.Spawner<Spawner<Name, Position, Velocity>>(), spawner => spawner.Spawn(new Name("a"), new Position(5, 6), new Velocity(7, 8)));

        Assert.That(world.Lookup<Name>()[spawned], Is.EqualTo(new Name("a")));
        Assert.That(world.Lookup<Position>()[spawned], Is.EqualTo(new Position(5, 6)));
        Assert.That(world.Lookup<Velocity>()[spawned], Is.EqualTo(new Velocity(7, 8)));
    }

    [Test]
    public void CommandsOnAnEntityDestroyedEarlierAreSkipped()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        world.Scheduler.Add(builder => ActionSystem.Of(builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity)));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Dead>(), builder.ComponentRemover<Position>(), builder.EntityDestroyer(), (dead, positions, destroyer) =>
        {
            dead.Add(entity, new Dead());
            positions.Remove(entity);
            destroyer.Destroy(entity);
        }));

        Assert.DoesNotThrow(world.Scheduler.Update);
        Assert.That(world.Lookup<Velocity>().Has(entity), Is.False);
    }

    [Test]
    public void AddingAPresentComponentReplacesItsValue()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Name("old"));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Name>(), names => names.Add(entity, new Name("new"))));

        world.Scheduler.Update();

        Assert.That(world.Lookup<Name>()[entity], Is.EqualTo(new Name("new")));
    }

    [Test]
    public void DestroyerForcesASyncPointBeforeALaterQuery()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position());
        world.AddComponent(entity, new Dead());
        world.Scheduler.Add(builder => ActionSystem.Of(builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity)));
        CorpseCounter counter = world.Scheduler.Add(builder => new CorpseCounter(builder.Query<Corpses>()));

        world.Scheduler.Update();

        Assert.That(counter.Count, Is.Zero);
    }

    [Test]
    public void RemovedSystemNoLongerRuns()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
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
        using TestWorld world = new TestWorld();
        world.Spawn(new Mass(1));
        Entity existing = world.Spawn(new Position(7, 7));
        List<Entity> spawned = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            spawned.Clear();
            for (int i = 0; i < count; i++)
            {
                spawned.Add(spawner.Spawn(new Position(i, 0), new Velocity(0, i)));
            }
        }));
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();

        UpdateWhileAQueryRuns(world);

        Assert.That(spawned.Where(positions.Has), Is.Empty);
        Assert.That(CountPositions(world), Is.EqualTo(1));
        Assert.That(positions[existing], Is.EqualTo(new Position(7, 7)));

        world.Scheduler.Update();

        Assert.That(spawned.All(positions.Has), Is.True);
        Assert.That(spawned.Select(entity => positions[entity].X), Is.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => world.Lookup<Velocity>().UnsafeGet(entity).Y), Is.EqualTo(Enumerable.Range(0, count).Select(i => (float)i)));
        Assert.That(CountPositions(world), Is.EqualTo(count + 1));
    }

    [Test]
    public void FailedSpawnKeepsNoReferenceToItsComponents()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Mass(1));
        WeakReference spawned = null!;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Name>>(), spawner => spawned = SpawnName(spawner)));

        UpdateWhileAQueryRuns(world);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(spawned.IsAlive, Is.False);
    }

    [Test]
    public void CommandsRecordedAfterAFailedOneAreDropped()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Mass(1));
        Entity entity = world.Spawn(new Position());
        bool record = true;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ComponentAdder<Name>(), (spawner, names) =>
        {
            if (record)
            {
                spawner.Spawn(default, default);
                names.Add(entity, new Name("dropped"));
            }
        }));

        UpdateWhileAQueryRuns(world);
        record = false;
        world.Scheduler.Update();

        Assert.That(world.Lookup<Name>().Has(entity), Is.False);
    }

    [Test]
    public void CommandsOfLaterSystemsAreDroppedWhenAnEarlierOneFails()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Mass(1));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<FrozenSpawner>(), earlier => earlier.Spawn(default)));
        List<Entity> recorded = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), later => recorded.Add(later.Spawn(default, default))));

        UpdateWhileAQueryRuns(world);
        world.Scheduler.Update();

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(positions.Has(recorded[0]), Is.False);
        Assert.That(positions.Has(recorded[1]), Is.True);
        Assert.That(CountPositions(world), Is.EqualTo(1));
    }

    [Test]
    public void SpawnerNamingAComponentTwiceTakingEntityOrExcludingThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Position, Position>>(), _ => { })));
        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Entity>>(), _ => { })));
        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Spawner<ExcludingSpawner>(), _ => { })));
    }

    [Test]
    public void SpawnerForcesASyncPointBeforeALaterQueryOfItsComponents()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
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
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position());
        int visible = -1;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Frozen>(), frozen => frozen.Add(entity, new Frozen())));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Query<UnfrozenPositions>(), positions =>
        {
            visible = 0;
            foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>> columns in positions)
            {
                visible += columns.Count;
            }
        }));

        world.Scheduler.Update();

        Assert.That(visible, Is.Zero);
    }

    [Test]
    public void AddingAComponentALaterQueryReadsAsOptionalForcesASyncPointBeforeIt()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position());
        bool sawVelocity = false;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Velocity>(), velocities => velocities.Add(entity, new Velocity(1, 0))));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Query<OptionalMovement>(), movement =>
        {
            foreach ((_, _, Optional<ReadOnlySpan<Velocity>> velocities) in movement)
            {
                sawVelocity = velocities.HasValue;
            }
        }));

        world.Scheduler.Update();

        Assert.That(sawVelocity, Is.True);
    }

    [Test]
    public void SpanWritesAreVisibleToLaterSystemsWithoutASyncPoint()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position(), new Velocity());
        Position seen = default;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Query<Movement>(), movement =>
        {
            foreach ((_, Span<Position> positions, _) in movement)
            {
                positions[0] = new Position(7, 8);
            }
        }));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Query<Positions>(), positions =>
        {
            foreach (Columns<ReadOnlySpan<Position>> columns in positions)
            {
                seen = columns.Item1[0];
            }
        }));

        world.Scheduler.Update();

        Assert.That(seen, Is.EqualTo(new Position(7, 8)));
    }

    [Test]
    public void RemoverRemovesAPresentComponentAndSkipsAnAbsentOne()
    {
        using TestWorld world = new TestWorld();
        Entity present = world.Spawn(new Position());
        Entity absent = world.Spawn(new Velocity());
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentRemover<Position>(), remover =>
        {
            remover.Remove(present);
            remover.Remove(absent);
        }));

        Assert.DoesNotThrow(world.Scheduler.Update);
        Assert.That(world.Lookup<Position>().Has(present), Is.False);
        Assert.That(world.Lookup<Position>().Has(absent), Is.False);
    }

    [Test]
    public void AddsAndRemovesOfOneComponentApplyInRecordingOrder()
    {
        using TestWorld world = new TestWorld();
        Entity removedLast = world.Spawn(new Position());
        Entity addedLast = world.Spawn(new Position());
        world.AddComponent(addedLast, new Dead());
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Dead>(), builder.ComponentRemover<Dead>(), (adder, remover) =>
        {
            adder.Add(removedLast, new Dead());
            remover.Remove(removedLast);
            remover.Remove(addedLast);
            adder.Add(addedLast, new Dead());
        }));

        world.Scheduler.Update();

        Assert.That(world.Lookup<Dead>().Has(removedLast), Is.False);
        Assert.That(world.Lookup<Dead>().Has(addedLast), Is.True);
    }

    [Test]
    public void SystemsRunInTheOrderTheyWereAdded()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
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
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Velocity());
        bool fail = true;
        Entity spawned = default;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Name>(), names =>
        {
            if (fail)
            {
                names.Add(entity, new Name("dropped"));
            }
        }));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            if (fail)
            {
                spawned = spawner.Spawn(default, default);
                throw new InvalidOperationException("The system failed.");
            }
        }));

        Assert.Throws<InvalidOperationException>(world.Scheduler.Update);
        fail = false;
        world.Scheduler.Update();

        Assert.That(world.Lookup<Name>().Has(entity), Is.False);
        Assert.That(world.Lookup<Position>().Has(spawned), Is.False);
        Assert.That(CountPositions(world), Is.Zero);

        // A released reservation frees its index, so the next entity reuses it with a newer version. ToString shows the index, which is internal.
        Entity next = world.Spawn(new Position());
        string spawnedIndex = spawned.ToString()[..spawned.ToString().IndexOf(',')];
        Assert.That(next.ToString(), Does.StartWith(spawnedIndex));
        Assert.That(next, Is.Not.EqualTo(spawned));
    }

    // Changes applied at a sync point before the failing system stay, while the commands the failing system recorded after it are dropped.
    [Test]
    public void SystemThatThrowsKeepsTheChangesOfEarlierSyncPoints()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        bool fail = true;
        Entity spawned = default;
        ComponentAdder<Name> keptNames = null!;
        world.Scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Query<Corpses>(), builder.ComponentAdder<Name>(), builder.Spawner<MovingSpawner>(), (_, names, spawner) =>
        {
            keptNames = names;
            if (fail)
            {
                names.Add(entity, new Name("dropped"));
                spawned = spawner.Spawn(default, default);
                throw new InvalidOperationException("The system failed.");
            }
        }));

        Assert.Throws<InvalidOperationException>(world.Scheduler.Update);

        Assert.That(world.Lookup<Dead>().Has(entity), Is.True);
        Assert.Throws<InvalidOperationException>(() => keptNames.Add(entity, new Name("kept")));

        fail = false;
        world.Scheduler.Update();

        Assert.That(world.Lookup<Name>().Has(entity), Is.False);
        Assert.That(world.Lookup<Position>().Has(spawned), Is.False);
    }

    [Test]
    public void WriterKeptPastUpdateThrows()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position());
        ComponentAdder<Dead> keptAdder = null!;
        MovingSpawner keptSpawner = null!;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Dead>(), builder.Spawner<MovingSpawner>(), (dead, spawner) =>
        {
            keptAdder = dead;
            keptSpawner = spawner;
        }));
        world.Scheduler.Update();

        Assert.Throws<InvalidOperationException>(() => keptAdder.Add(entity, new Dead()));
        Assert.Throws<InvalidOperationException>(() => keptSpawner.Spawn(default, default));
        world.Scheduler.Update();
        Assert.That(world.Lookup<Dead>().Has(entity), Is.False);
    }

    // A system gets its writers and lookups in its constructor, but they work only during its Update.
    [Test]
    public void WriterOrLookupUsedWhileItsSystemIsCreatedThrows()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());

        Assert.Throws<InvalidOperationException>(() => world.Scheduler.Add(builder =>
        {
            builder.ComponentAdder<Dead>().Add(entity, new Dead());
            return new ActionSystem(() => { });
        }));
        Assert.Throws<InvalidOperationException>(() => world.Scheduler.Add(builder =>
        {
            builder.Spawner<MovingSpawner>().Spawn(default, default);
            return new ActionSystem(() => { });
        }));
        Assert.Throws<InvalidOperationException>(() => world.Scheduler.Add(builder =>
        {
            _ = builder.ReadOnlyComponentLookup<Position>().Has(entity);
            return new ActionSystem(() => { });
        }));
        world.Scheduler.Update();

        Assert.That(world.Lookup<Dead>().Has(entity), Is.False);
        Assert.That(CountPositions(world), Is.EqualTo(1));
    }

    [Test]
    public void LookupReadsAndWritesTheComponentsOfAGivenEntity()
    {
        using TestWorld world = new TestWorld();
        Entity moving = world.Spawn(new Position(), new Velocity(1, 2));
        Entity still = world.Spawn(new Position(5, 5));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position>(), builder.ReadOnlyComponentLookup<Velocity>(), (positions, velocities) =>
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

        world.Scheduler.Update();

        Assert.That(world.Lookup<Position>()[moving], Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Lookup<Position>()[still], Is.EqualTo(new Position(5, 5)));
    }

    [Test]
    public void LookupSeesTheChangesOfAnEarlierSystemInTheSameUpdate()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        bool sawDead = false;
        world.Scheduler.Add(builder => new Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<Dead>(), dead => sawDead = dead.Has(entity)));

        world.Scheduler.Update();

        Assert.That(sawDead, Is.True);
    }

    [Test]
    public void LookupHasIsFalseForADestroyedEntity()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        world.Destroy(entity);
        bool has = true;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<Position>(), positions => has = positions.Has(entity)));

        world.Scheduler.Update();

        Assert.That(has, Is.False);
    }

    [Test]
    public void LookupOfSeveralComponentsReadsAndWritesEachOfThem()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity(1, 2));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position, Velocity>(), lookup =>
        {
            ComponentRefs<Position, Velocity> components = lookup[entity];
            components.Item1 = new Position(components.Item1.X + components.Item2.X, components.Item1.Y + components.Item2.Y);
            components.Item2 = default;
        }));

        world.Scheduler.Update();

        Assert.That(world.Lookup<Position>()[entity], Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Lookup<Velocity>()[entity], Is.EqualTo(default(Velocity)));
    }

    [Test]
    public void LookupOfSeveralComponentsHasOnlyAnAliveEntityWithAllOfThem()
    {
        using TestWorld world = new TestWorld();
        Entity frozen = world.Spawn(new Position(), new Velocity());
        world.AddComponent(frozen, new Frozen());
        Entity moving = world.Spawn(new Position(), new Velocity());
        Entity destroyed = world.Spawn(new Position(), new Velocity());
        world.AddComponent(destroyed, new Frozen());
        world.Destroy(destroyed);
        bool[] has = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<Position, Frozen>(), lookup =>
        {
            has = [lookup.Has(frozen), lookup.Has(moving), lookup.Has(destroyed)];
            Assert.Throws<InvalidOperationException>(() => { _ = lookup[moving]; });
            Assert.Throws<ArgumentException>(() => { _ = lookup[destroyed]; });
        }));

        world.Scheduler.Update();

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
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(), new Velocity());
        ComponentLookup<Position> keptPositions = null!;
        ReadOnlyComponentLookup<Velocity> keptVelocities = null!;
        ReadOnlyComponentLookup<Position, Velocity> keptBoth = null!;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position>(), builder.ReadOnlyComponentLookup<Velocity>(), builder.ReadOnlyComponentLookup<Position, Velocity>(), (positions, velocities, both) =>
        {
            keptPositions = positions;
            keptVelocities = velocities;
            keptBoth = both;
        }));
        world.Scheduler.Update();

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

    // Copying a spawn's components cannot fail, so the tests fail a sync point by running it while code outside systems runs a query, where its first
    // command throws. A query runs only over an archetype with entities, so the test spawns an entity with Mass before it adds its systems.
    private static void UpdateWhileAQueryRuns(TestWorld world)
    {
        int runs = 0;
        foreach (Columns<ReadOnlySpan<Mass>> _ in world.Get<Masses>())
        {
            runs++;
            Assert.Throws<InvalidOperationException>(world.Scheduler.Update);
        }

        Assert.That(runs, Is.EqualTo(1));
    }

    private static int CountPositions(TestWorld world)
    {
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>> columns in world.Get<UnfrozenPositions>())
        {
            count += columns.Count;
        }

        return count;
    }
}
