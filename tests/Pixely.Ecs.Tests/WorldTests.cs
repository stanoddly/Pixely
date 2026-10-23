namespace Pixely.Ecs.Tests;

public class WorldTests
{
    [Test]
    public void CreatedEntitiesAreDistinctAndAlive()
    {
        World world = new World();

        Entity first = world.Create();
        Entity second = world.Create();

        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(world.IsAlive(first), Is.True);
        Assert.That(world.IsAlive(second), Is.True);
    }

    [Test]
    public void DefaultEntityIsNotAlive()
    {
        World world = new World();
        world.Create();

        Assert.That(world.IsAlive(default), Is.False);
    }

    [Test]
    public void DestroyedEntityStaysDeadAfterItsIndexIsReused()
    {
        World world = new World();
        Entity destroyed = world.Create();
        world.Destroy(destroyed);

        Entity reused = world.Create();

        Assert.That(world.IsAlive(destroyed), Is.False);
        Assert.That(world.IsAlive(reused), Is.True);
        Assert.That(reused, Is.Not.EqualTo(destroyed));
    }

    [Test]
    public void OperationsOnDeadEntityThrow()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Destroy(entity);

        Assert.Throws<ArgumentException>(() => world.Destroy(entity));
        Assert.Throws<ArgumentException>(() => world.Add(entity, new Position()));
        Assert.Throws<ArgumentException>(() => world.Remove<Position>(entity));
        Assert.Throws<ArgumentException>(() => world.Has<Position>(entity));
        Assert.Throws<ArgumentException>(() => world.Get<Position>(entity));
    }

    [Test]
    public void GetReturnsReferenceToStoredComponent()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position(1, 2));

        world.Get<Position>(entity).X = 5;

        Assert.That(world.Get<Position>(entity), Is.EqualTo(new Position(5, 2)));
    }

    [Test]
    public void AddingAndRemovingKeepsOtherComponents()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position(1, 2));
        world.Add(entity, new Velocity(3, 4));
        world.Add(entity, new Name("dude"));

        world.Remove<Velocity>(entity);

        Assert.That(world.Has<Velocity>(entity), Is.False);
        Assert.That(world.Get<Position>(entity), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Name>(entity), Is.EqualTo(new Name("dude")));
    }

    [Test]
    public void AddingPresentComponentThrows()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position());

        Assert.Throws<InvalidOperationException>(() => world.Add(entity, new Position()));
    }

    [Test]
    public void RemovingOrGettingAbsentComponentThrows()
    {
        World world = new World();
        Entity entity = world.Create();

        Assert.Throws<InvalidOperationException>(() => world.Remove<Position>(entity));
        Assert.Throws<InvalidOperationException>(() => world.Get<Position>(entity));
    }

    // No other test uses this component, so its id is allocated after the test creates its archetype.
    private struct FirstUsedLate;

    [Test]
    public void ComponentFirstUsedAfterTheArchetypeIsAbsentAndCanBeAdded()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position(1, 2));

        Assert.That(world.Has<FirstUsedLate>(entity), Is.False);
        Assert.Throws<InvalidOperationException>(() => world.Get<FirstUsedLate>(entity));

        world.Add(entity, new FirstUsedLate());

        Assert.That(world.Has<FirstUsedLate>(entity), Is.True);
        Assert.That(world.Get<Position>(entity), Is.EqualTo(new Position(1, 2)));
    }

    [Test]
    public void EntitiesKeepTheirComponentsWhenAnotherLeavesTheArchetype()
    {
        World world = new World();
        Entity[] entities = new Entity[4];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.Create();
            world.Add(entities[i], new Position(i, i));
        }

        world.Destroy(entities[0]);
        world.Remove<Position>(entities[1]);

        Assert.That(world.Get<Position>(entities[2]), Is.EqualTo(new Position(2, 2)));
        Assert.That(world.Get<Position>(entities[3]), Is.EqualTo(new Position(3, 3)));
        Assert.That(world.Has<Position>(entities[1]), Is.False);
    }
}
