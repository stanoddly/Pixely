using System.Runtime.CompilerServices;

namespace Pixely.Ecs.Tests;

public class CapacityTests
{
    [Test]
    public void EnsureRoomForGrowsAnEmptyArchetypeToExactlyTheRoomAndNeverShrinksIt()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner =>
        {
            spawner.EnsureRoomFor(1000);
            return spawner.Spawn(default, default);
        });

        world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.EnsureRoomFor(10));

        Assert.That(MeasureCapacity(world, first), Is.EqualTo(1000));
    }

    // A reference stays where it is only while the columns are not replaced, so it shows that spawning within the room grows nothing.
    [Test]
    public void EntitiesSpawnedWithinTheRoomDoNotGrowTheColumns()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner =>
        {
            spawner.EnsureRoomFor(100);
            return spawner.Spawn(new Position(0, 0), new Velocity(1, 0));
        });
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        ref readonly Position position = ref positions[first];

        for (int i = 1; i < 100; i++)
        {
            world.Spawn(new Position(i, 0), new Velocity(1, 0));
        }

        Assert.That(Unsafe.AreSame(ref Unsafe.AsRef(in position), ref Unsafe.AsRef(in positions[first])), Is.True);
        Assert.That(MeasureCapacity(world, first), Is.EqualTo(100));
    }

    [Test]
    public void EnsureRoomForKeepsTheEntitiesAndTheirComponents()
    {
        using TestWorld world = new TestWorld();
        Entity[] entities = [.. Enumerable.Range(0, 20).Select(i => world.Spawn(new Position(i, 0), new Velocity(1, 0)))];

        world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.EnsureRoomFor(5000));

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(entities.Select(entity => positions[entity].X), Is.EqualTo(Enumerable.Range(0, 20).Select(i => (float)i)));
    }

    // The room is made at the sync point, which throws while code outside systems runs a query.
    [Test]
    public void EnsureRoomForThrowsForANegativeCountAndItsSyncPointThrowsWhileAQueryRuns()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position(), new Velocity());

        Assert.Throws<ArgumentOutOfRangeException>(() => world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.EnsureRoomFor(-1)));
        int runs = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> _ in world.Get<MovingEntities>())
        {
            runs++;
            Assert.Throws<InvalidOperationException>(() => world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => spawner.EnsureRoomFor(100)));
        }

        Assert.That(runs, Is.EqualTo(1));
    }

    // The spawner's request waits for the sync point, so it never replaces the arrays under a running query, even in a frame without spawns.
    [TestCase(0)]
    [TestCase(3)]
    public void SpawnerMakesRoomAtTheSyncPoint(int spawns)
    {
        using TestWorld world = new TestWorld();
        Entity existing = world.Spawn(new Position(), new Velocity());
        bool grewDuringUpdate = true;
        ActionSystem system = world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.ReadOnlyComponentLookup<Position>(), (spawner, positions) =>
        {
            ref readonly Position before = ref positions[existing];
            spawner.EnsureRoomFor(500);
            for (int i = 0; i < spawns; i++)
            {
                spawner.Spawn(new Position(i, 0), default);
            }

            grewDuringUpdate = !Unsafe.AreSame(ref Unsafe.AsRef(in before), ref Unsafe.AsRef(in positions[existing]));
        }));

        world.Scheduler.Update();
        world.Scheduler.Remove(system);

        Assert.That(grewDuringUpdate, Is.False);
        Assert.That(MeasureCapacity(world, existing), Is.EqualTo(501));
    }

    // Each system asks for room for its own spawns, so together they grow the archetype to all of them, each before its spawns.
    [Test]
    public void SpawnerRoomAddsToTheEntitiesOfEarlierSystems()
    {
        using TestWorld world = new TestWorld();
        Entity existing = world.Spawn(new Position(0, 0), new Velocity(1, 0));
        List<ActionSystem> systems = [];
        for (int system = 0; system < 2; system++)
        {
            systems.Add(world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
            {
                spawner.EnsureRoomFor(100);
                for (int i = 0; i < 100; i++)
                {
                    spawner.Spawn(new Position(i, 0), default);
                }
            })));
        }

        world.Scheduler.Update();
        systems.ForEach(world.Scheduler.Remove);

        Assert.That(MeasureCapacity(world, existing), Is.EqualTo(201));
        Assert.That(world.Lookup<Position>()[existing], Is.EqualTo(new Position(0, 0)));
    }

    // Both requests come before the spawns, so each must add to the other rather than to the entities the archetype holds when it applies.
    [Test]
    public void RoomOfSpawnersSharingAnArchetypeAddsUpWhateverTheOrder()
    {
        using TestWorld world = new TestWorld();
        Entity existing = world.Spawn(new Position(0, 0), new Velocity(1, 0));
        world.Scheduler.RunOnce(builder => (builder.Spawner<MovingSpawner>(), builder.Spawner<SecondMovingSpawner>()), spawners =>
        {
            (MovingSpawner first, SecondMovingSpawner second) = spawners;
            first.EnsureRoomFor(100);
            second.EnsureRoomFor(60);
            second.EnsureRoomFor(40);
            for (int i = 0; i < 100; i++)
            {
                first.Spawn(new Position(i, 0), default);
                second.Spawn(new Position(-i, 0), default);
            }
        });

        Assert.That(MeasureCapacity(world, existing), Is.EqualTo(201));
        Assert.That(world.Lookup<Position>()[existing], Is.EqualTo(new Position(0, 0)));
    }

    [Test]
    public void SpawnerRequestOfASystemThatThrowsIsDropped()
    {
        using TestWorld world = new TestWorld();
        Entity existing = world.Spawn(new Position(), new Velocity());
        bool fail = true;
        ActionSystem system = world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            if (fail)
            {
                spawner.EnsureRoomFor(500);
                throw new InvalidOperationException("The system failed.");
            }
        }));

        Assert.Throws<InvalidOperationException>(world.Scheduler.Update);
        fail = false;
        world.Scheduler.Update();
        world.Scheduler.Remove(system);

        Assert.That(MeasureCapacity(world, existing), Is.LessThan(500));
    }

    [Test]
    public void SpawnerRequestOutsideUpdateThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        MovingSpawner kept = null!;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner => kept = spawner));
        scheduler.Update();

        Assert.Throws<InvalidOperationException>(() => kept.EnsureRoomFor(500));
    }

    // The rows the archetype of MovingSpawner holds before it grows. Code outside systems cannot read the capacity, but a growth replaces the columns,
    // which moves the Position of the entity in that archetype, so this spawns one entity per update until it moves. No other system may spawn there.
    private static int MeasureCapacity(TestWorld world, Entity entity)
    {
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> columns in world.Get<MovingEntities>())
        {
            count += columns.Count;
        }

        ActionSystem spawning = world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner => spawner.Spawn(default, default)));
        try
        {
            while (true)
            {
                ref readonly Position before = ref positions[entity];
                world.Scheduler.Update();
                if (!Unsafe.AreSame(ref Unsafe.AsRef(in before), ref Unsafe.AsRef(in positions[entity])))
                {
                    return count;
                }

                count++;
            }
        }
        finally
        {
            world.Scheduler.Remove(spawning);
        }
    }
}
