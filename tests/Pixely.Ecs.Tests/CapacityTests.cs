using System.Runtime.CompilerServices;

namespace Pixely.Ecs.Tests;

public class CapacityTests
{
    [Test]
    public void EnsureCapacityGrowsASmallerArchetypeToExactlyTheCapacityAndNeverShrinksIt()
    {
        World world = new World();

        Assert.That(world.EnsureCapacity<MovingSpawner>(1000), Is.EqualTo(1000));
        Assert.That(world.EnsureCapacity<MovingSpawner>(10), Is.EqualTo(1000));
    }

    // A reference stays where it is only while the columns are not replaced, so it shows that adding entities within the capacity grows nothing.
    [Test]
    public void EntitiesAddedWithinTheCapacityDoNotGrowTheColumns()
    {
        World world = new World();
        world.EnsureCapacity<MovingSpawner>(100);
        Entity first = CreateMoving(world, 0);
        ref Position position = ref world.Get<Position>(first);

        for (int i = 1; i < 100; i++)
        {
            CreateMoving(world, i);
        }

        Assert.That(Unsafe.AreSame(ref position, ref world.Get<Position>(first)), Is.True);
        Assert.That(world.EnsureCapacity<MovingSpawner>(0), Is.EqualTo(100));
    }

    [Test]
    public void EnsureCapacityKeepsTheEntitiesAndTheirComponents()
    {
        World world = new World();
        Entity[] entities = [.. Enumerable.Range(0, 20).Select(i => CreateMoving(world, i))];

        world.EnsureCapacity<MovingSpawner>(5000);

        Assert.That(entities.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, 20).Select(i => (float)i)));
    }

    [Test]
    public void EnsureCapacityThrowsWhileAQueryRunsAndForANegativeCapacity()
    {
        World world = new World();
        CreateMoving(world, 0);

        Assert.Throws<ArgumentOutOfRangeException>(() => world.EnsureCapacity<MovingSpawner>(-1));
        foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> _ in world.Query<Movement>())
        {
            Assert.Throws<InvalidOperationException>(() => world.EnsureCapacity<MovingSpawner>(100));
        }
    }

    // The spawner's request waits for the sync point, so it never replaces the arrays under a running query, even in a frame without spawns.
    [TestCase(0)]
    [TestCase(3)]
    public void SpawnerMakesRoomAtTheSyncPoint(int spawns)
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        int capacityDuringUpdate = 0;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            spawner.EnsureRoomFor(500);
            for (int i = 0; i < spawns; i++)
            {
                spawner.Spawn(new Position(i, 0), default);
            }

            capacityDuringUpdate = world.EnsureCapacity<MovingSpawner>(0);
        }));

        scheduler.Update();

        Assert.That(capacityDuringUpdate, Is.LessThan(500));
        Assert.That(world.EnsureCapacity<MovingSpawner>(0), Is.EqualTo(500));
    }

    // Each system asks for room for its own spawns, so together they grow the archetype to all of them, each before its spawns.
    [Test]
    public void SpawnerRoomAddsToTheEntitiesOfEarlierSystems()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity existing = CreateMoving(world, 0);
        for (int system = 0; system < 2; system++)
        {
            scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
            {
                spawner.EnsureRoomFor(100);
                for (int i = 0; i < 100; i++)
                {
                    spawner.Spawn(new Position(i, 0), default);
                }
            }));
        }

        scheduler.Update();

        Assert.That(world.EnsureCapacity<MovingSpawner>(0), Is.EqualTo(201));
        Assert.That(world.Get<Position>(existing), Is.EqualTo(new Position(0, 0)));
    }

    // Both requests come before the spawns, so each must add to the other rather than to the entities the archetype holds when it applies.
    [Test]
    public void RoomOfSpawnersSharingAnArchetypeAddsUpWhateverTheOrder()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity existing = CreateMoving(world, 0);
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), builder.Spawner<SecondMovingSpawner>(), (first, second) =>
        {
            first.EnsureRoomFor(100);
            second.EnsureRoomFor(60);
            second.EnsureRoomFor(40);
            for (int i = 0; i < 100; i++)
            {
                first.Spawn(new Position(i, 0), default);
                second.Spawn(new Position(-i, 0), default);
            }
        }));

        scheduler.Update();

        Assert.That(world.EnsureCapacity<SecondMovingSpawner>(0), Is.EqualTo(201));
        Assert.That(world.Get<Position>(existing), Is.EqualTo(new Position(0, 0)));
    }

    [Test]
    public void SpawnerRequestOfASystemThatThrowsIsDropped()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        bool fail = true;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            if (fail)
            {
                spawner.EnsureRoomFor(500);
                throw new InvalidOperationException("The system failed.");
            }
        }));

        Assert.Throws<InvalidOperationException>(scheduler.Update);
        fail = false;
        scheduler.Update();

        Assert.That(world.EnsureCapacity<MovingSpawner>(0), Is.LessThan(500));
    }

    [Test]
    public void SpawnerRequestOutsideUpdateThrows()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        MovingSpawner kept = null!;
        scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner => kept = spawner));
        scheduler.Update();

        Assert.Throws<InvalidOperationException>(() => kept.EnsureRoomFor(500));
    }

    private static Entity CreateMoving(World world, int i)
    {
        Entity entity = world.Create();
        world.Add(entity, new Position(i, 0));
        world.Add(entity, new Velocity(1, 0));
        return entity;
    }
}
