using System.Runtime.CompilerServices;

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
    public void EntityOfAnotherWorldPastItsEntitiesIsNotAlive()
    {
        World other = new World();
        Entity entity = default;
        for (int i = 0; i < 10; i++)
        {
            entity = other.Create();
        }

        World world = new World();
        world.Create();

        Assert.That(world.IsAlive(entity), Is.False);
        Assert.Throws<ArgumentException>(() => world.Destroy(entity));
    }

    // Takes about 15 seconds in Release and minutes in Debug: the index is reused until its version runs out.
    [Test, Explicit]
    public void IndexIsRetiredWhenItsVersionRunsOut()
    {
        World world = new World();
        Entity first = world.Create();
        Entity entity = first;
        for (int version = 1; version < int.MaxValue; version++)
        {
            world.Destroy(entity);
            entity = world.Create();
        }

        Assert.That(entity.ToString(), Is.EqualTo($"Entity {{ Index=0, Version={int.MaxValue} }}"));
        world.Destroy(entity);

        Entity next = world.Create();

        Assert.That(next.ToString(), Does.StartWith("Entity { Index=1,"));
        Assert.That(world.IsAlive(first), Is.False);
    }

    [Test]
    public void UnsafeGetReturnsTheReferenceGetReturns()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position(1, 2));
        world.Add(entity, new Velocity(3, 4));

        world.UnsafeGet<Velocity>(entity).X = 5;

        Assert.That(Unsafe.AreSame(ref world.UnsafeGet<Position>(entity), ref world.Get<Position>(entity)), Is.True);
        Assert.That(world.Get<Velocity>(entity), Is.EqualTo(new Velocity(5, 4)));
    }

    [Test]
    public void EntitiesAreEqualOnlyWithTheSameIndexAndVersion()
    {
        World world = new World();
        Entity destroyed = world.Create();
        world.Destroy(destroyed);
        Entity reused = world.Create();
        Entity copy = reused;

        Assert.That(reused == copy, Is.True);
        Assert.That(reused != destroyed, Is.True);
        Assert.That(reused.Equals((object)copy), Is.True);
        Assert.That(reused.Equals((object)destroyed), Is.False);
        Assert.That(reused.GetHashCode(), Is.EqualTo(copy.GetHashCode()));
        Assert.That(reused.ToString(), Is.EqualTo("Entity { Index=0, Version=2 }"));
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

    // A column starts with room for four chunks and grows that array as the archetype adds chunks, so Get must not keep reading the first one.
    [Test]
    public void GetAndUnsafeGetReadEveryRowAfterTheArchetypeAddsManyChunks()
    {
        World world = new World();
        Entity[] entities = new Entity[20_000];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.Create();
            world.Add(entities[i], new Position(i, 0));
        }

        for (int i = 0; i < entities.Length; i++)
        {
            Assert.That(world.Get<Position>(entities[i]).X, Is.EqualTo(i));
            Assert.That(world.UnsafeGet<Position>(entities[i]).X, Is.EqualTo(i));
        }
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
    public void MovingTheLastRowOfAnArchetypeKeepsEveryRecord()
    {
        World world = new World();
        Entity first = world.Create();
        world.Add(first, new Position(1, 2));
        Entity last = world.Create();
        world.Add(last, new Position(3, 4));

        world.Add(last, new Velocity(5, 6));

        Assert.That(world.Get<Position>(first), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Position>(last), Is.EqualTo(new Position(3, 4)));
        Assert.That(world.Get<Velocity>(last), Is.EqualTo(new Velocity(5, 6)));

        world.Remove<Velocity>(last);

        Assert.That(world.Get<Position>(first), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Get<Position>(last), Is.EqualTo(new Position(3, 4)));
        Assert.That(world.Has<Velocity>(last), Is.False);
    }

    [Test]
    public void RemovingAReferenceComponentReleasesOnlyTheRemovedValue()
    {
        World world = new World();
        Entity first = world.Create();
        WeakReference removed = AddUniqueName(world, first, 'a');
        Entity last = world.Create();
        world.Add(last, new Name(new string('b', 100)));

        world.Remove<Name>(first);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(removed.IsAlive, Is.False);
        Assert.That(world.Get<Name>(last), Is.EqualTo(new Name(new string('b', 100))));
    }

    // No other test uses this component, so its id is past the edge arrays of every archetype the test creates.
    private struct AddedAndRemovedLate;

    [Test]
    public void ComponentFirstUsedAfterTheArchetypeCanBeAddedAndRemovedAgain()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Position(1, 2));

        world.Add(entity, new AddedAndRemovedLate());
        world.Remove<AddedAndRemovedLate>(entity);
        world.Add(entity, new AddedAndRemovedLate());

        Assert.That(world.Has<AddedAndRemovedLate>(entity), Is.True);
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

    // Creates the string here, so nothing but the world references it once this returns.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddUniqueName(World world, Entity entity, char character)
    {
        string value = new string(character, 100);
        world.Add(entity, new Name(value));
        return new WeakReference(value);
    }
}
