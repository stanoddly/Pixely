namespace Pixely.Ecs.Tests;

public class StorageTests
{
    // An archetype stores each component in one array, so a query yields one span per archetype.
    [Test]
    public void QueryVisitsEveryEntityOfAnArchetypeInOneSpan()
    {
        World world = new World();
        Entity[] entities = CreateMoving(world, 1000);

        List<int> counts = [];
        HashSet<Entity> visited = [];
        foreach ((ReadOnlySpan<Entity> matched, Span<Position> positions, ReadOnlySpan<Velocity> velocities) in world.Query<Movement>())
        {
            counts.Add(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                positions[i].X += velocities[i].X;
                visited.Add(matched[i]);
            }
        }

        Assert.That(counts, Is.EqualTo(new[] { 1000 }));
        Assert.That(visited.SetEquals(entities), Is.True);
        Assert.That(entities.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(Enumerable.Range(1, 1000).Select(i => (float)i)));
    }

    [Test]
    public void DestroyingMovesTheLastRowIntoTheGapAndKeepsEveryValue()
    {
        World world = new World();
        Entity[] entities = CreateMoving(world, 1000);

        for (int i = 0; i < 400; i++)
        {
            world.Destroy(entities[i * 2]);
        }

        for (int i = 0; i < 1000; i++)
        {
            bool destroyed = i < 800 && i % 2 == 0;
            Assert.That(world.IsAlive(entities[i]), Is.EqualTo(!destroyed));
            if (!destroyed)
            {
                Assert.That(world.Get<Position>(entities[i]), Is.EqualTo(new Position(i, 0)));
            }
        }

        Assert.That(CountMovement(world), Is.EqualTo(600));
    }

    // Both archetypes grow while entities move between them, so moves read from and write to replaced arrays.
    [Test]
    public void AddingAndRemovingWhileArchetypesGrowKeepsEveryValue()
    {
        World world = new World();
        Entity[] entities = new Entity[1500];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.Create();
            world.Add(entities[i], new Position(i, -i));
        }

        for (int i = 0; i < entities.Length; i += 2)
        {
            world.Add(entities[i], new Velocity(i + 1, i + 2));
        }

        for (int i = 0; i < entities.Length; i += 4)
        {
            world.Remove<Velocity>(entities[i]);
        }

        for (int i = 0; i < entities.Length; i++)
        {
            bool hasVelocity = i % 2 == 0 && i % 4 != 0;
            Assert.That(world.Get<Position>(entities[i]), Is.EqualTo(new Position(i, -i)));
            Assert.That(world.Has<Velocity>(entities[i]), Is.EqualTo(hasVelocity));
            if (hasVelocity)
            {
                Assert.That(world.Get<Velocity>(entities[i]), Is.EqualTo(new Velocity(i + 1, i + 2)));
            }
        }

        Assert.That(CountMovement(world), Is.EqualTo(375));
    }

    [Test]
    public void SpawnBatchGrowsAPartlyFilledArchetype()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        List<Entity> spawned = [];
        int count = 3;
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>>(spawner =>
        {
            for (int i = 0; i < count; i++)
            {
                spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(spawned.Count, 0), Velocity = new Velocity(1, 0) }));
            }
        }));

        scheduler.Update();
        count = 2000;
        scheduler.Update();

        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, 2003).Select(i => (float)i)));
        Assert.That(CountMovement(world), Is.EqualTo(2003));
    }

    // 10,000 entities need more than the four chunks each list of the spawner starts with room for, and grow the columns many times.
    [Test]
    public void SpawningPastTheInitialCapacityKeepsEveryValue()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        List<Entity> spawned = [];
        scheduler.Add(new ActionSystem<Spawner<MovingBlueprint>>(spawner =>
        {
            for (int i = 0; i < 10000; i++)
            {
                spawned.Add(spawner.Spawn(new MovingBlueprint { Position = new Position(i, 0), Velocity = new Velocity(0, i) }));
            }
        }));

        scheduler.Update();

        Assert.That(CountMovement(world), Is.EqualTo(10000));
        Assert.That(spawned.Select(entity => world.Get<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => world.Get<Velocity>(entity).Y), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => world.UnsafeGet<Position>(entity).X), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
        Assert.That(spawned.Select(entity => world.UnsafeGet<Velocity>(entity).Y), Is.EqualTo(Enumerable.Range(0, 10000).Select(i => (float)i)));
    }

    private static Entity[] CreateMoving(World world, int count)
    {
        Entity[] entities = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            entities[i] = world.Create();
            world.Add(entities[i], new Position(i, 0));
            world.Add(entities[i], new Velocity(1, 0));
        }

        return entities;
    }

    private static int CountMovement(World world)
    {
        int count = 0;
        foreach (Columns<ReadOnlySpan<Entity>, Span<Position>, ReadOnlySpan<Velocity>> columns in world.Query<Movement>())
        {
            count += columns.Count;
        }

        return count;
    }
}
