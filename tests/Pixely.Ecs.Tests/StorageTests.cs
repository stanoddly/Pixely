namespace Pixely.Ecs.Tests;

public class StorageTests
{
    // An archetype stores each component in one array, so a query yields one span per archetype.
    [Test]
    public void QueryVisitsEveryEntityOfAnArchetypeInOneSpan()
    {
        using TestWorld world = new TestWorld();
        Entity[] entities = SpawnMoving(world, 1000);

        List<int> counts = [];
        HashSet<Entity> visited = [];
        world.Scheduler.RunOnce(builder => builder.Query<Movement>(), movement =>
        {
            foreach ((ReadOnlySpan<Entity> matched, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in movement)
            {
                counts.Add(positions.Length);
                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i].X += velocities[i].X;
                    visited.Add(matched[i]);
                }
            }
        });

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(counts, Is.EqualTo(new[] { 1000 }));
        Assert.That(visited.SetEquals(entities), Is.True);
        Assert.That(entities.Select(entity => positions[entity].X), Is.EqualTo(Enumerable.Range(1, 1000).Select(i => (float)i)));
    }

    [Test]
    public void DestroyingMovesTheLastRowIntoTheGapAndKeepsEveryValue()
    {
        using TestWorld world = new TestWorld();
        Entity[] entities = SpawnMoving(world, 1000);

        world.Scheduler.RunOnce(builder => builder.EntityDestroyer(), destroyer =>
        {
            for (int i = 0; i < 400; i++)
            {
                destroyer.Destroy(entities[i * 2]);
            }
        });

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        for (int i = 0; i < 1000; i++)
        {
            bool destroyed = i < 800 && i % 2 == 0;
            Assert.That(positions.Has(entities[i]), Is.EqualTo(!destroyed));
            if (!destroyed)
            {
                Assert.That(positions[entities[i]], Is.EqualTo(new Position(i, 0)));
            }
        }

        Assert.That(CountMoving(world), Is.EqualTo(600));
    }

    // Both archetypes grow while entities move between them, so moves read from and write to replaced arrays.
    [Test]
    public void AddingAndRemovingWhileArchetypesGrowKeepsEveryValue()
    {
        using TestWorld world = new TestWorld();
        Entity[] entities = world.Scheduler.RunOnce(builder => builder.Spawner<Spawner<Position>>(), spawner =>
            Enumerable.Range(0, 1500).Select(i => spawner.Spawn(new Position(i, -i))).ToArray());

        world.Scheduler.RunOnce(builder => builder.ComponentAdder<Velocity>(), velocities =>
        {
            for (int i = 0; i < entities.Length; i += 2)
            {
                velocities.Add(entities[i], new Velocity(i + 1, i + 2));
            }
        });

        world.Scheduler.RunOnce(builder => builder.ComponentRemover<Velocity>(), velocities =>
        {
            for (int i = 0; i < entities.Length; i += 4)
            {
                velocities.Remove(entities[i]);
            }
        });

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        ReadOnlyComponentLookup<Velocity> velocities = world.Lookup<Velocity>();
        for (int i = 0; i < entities.Length; i++)
        {
            bool hasVelocity = i % 2 == 0 && i % 4 != 0;
            Assert.That(positions[entities[i]], Is.EqualTo(new Position(i, -i)));
            Assert.That(velocities.Has(entities[i]), Is.EqualTo(hasVelocity));
            if (hasVelocity)
            {
                Assert.That(velocities[entities[i]], Is.EqualTo(new Velocity(i + 1, i + 2)));
            }
        }

        Assert.That(CountMoving(world), Is.EqualTo(375));
    }

    [Test]
    public void SpawnBatchGrowsAPartlyFilledArchetype()
    {
        using TestWorld world = new TestWorld();
        List<Entity> spawned = [];
        int count = 3;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            for (int i = 0; i < count; i++)
            {
                spawned.Add(spawner.Spawn(new Position(spawned.Count, 0), new Velocity(1, 0)));
            }
        }));

        world.Scheduler.Update();
        count = 2000;
        world.Scheduler.Update();

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(spawned.Select(entity => positions[entity].X), Is.EqualTo(Enumerable.Range(0, 2003).Select(i => (float)i)));
        Assert.That(CountMoving(world), Is.EqualTo(2003));
    }

    // 10,000 entities need more than the four chunks each list of the spawner starts with room for, and grow the columns many times.
    [Test]
    public void SpawningPastTheInitialCapacityKeepsEveryValue()
    {
        using TestWorld world = new TestWorld();
        List<Entity> spawned = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<MovingSpawner>(), spawner =>
        {
            for (int i = 0; i < 10000; i++)
            {
                spawned.Add(spawner.Spawn(new Position(i, 0), new Velocity(0, i)));
            }
        }));

        world.Scheduler.Update();

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        ReadOnlyComponentLookup<Velocity> velocities = world.Lookup<Velocity>();
        Assert.That(CountMoving(world), Is.EqualTo(10000));
        Assert.That(spawned.Select(entity => positions[entity].X), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => velocities[entity].Y), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => positions.UnsafeGet(entity).X), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => velocities.UnsafeGet(entity).Y), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
    }

    private static Entity[] SpawnMoving(TestWorld world, int count) =>
        world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner => Enumerable.Range(0, count).Select(i => spawner.Spawn(new Position(i, 0), new Velocity(1, 0))).ToArray());

    private static int CountMoving(TestWorld world)
    {
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, ReadOnlySpan<Position>, ReadOnlySpan<Velocity>> columns in world.Get<MovingEntities>())
        {
            count += columns.Count;
        }

        return count;
    }
}
