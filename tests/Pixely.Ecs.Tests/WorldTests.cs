using System.Runtime.CompilerServices;

namespace Pixely.Ecs.Tests;

public class WorldTests
{
    [Test]
    public void SpawnedEntitiesAreDistinctAndAlive()
    {
        using TestWorld world = new TestWorld();

        Entity first = world.Spawn(new Position());
        Entity second = world.Spawn(new Position());

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(positions.Has(first), Is.True);
        Assert.That(positions.Has(second), Is.True);
    }

    [Test]
    public void DefaultEntityIsNotAlive()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position());

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(positions.Has(default), Is.False);
        Assert.Throws<ArgumentException>(() => _ = positions[default]);
    }

    [Test]
    public void EntityOfAnotherWorldPastItsEntitiesIsNotAlive()
    {
        using TestWorld other = new TestWorld();
        Entity entity = other.Scheduler.RunOnce(builder => builder.Spawner<Spawner<Position>>(), spawner =>
        {
            Entity last = default;
            for (int i = 0; i < 10; i++)
            {
                last = spawner.Spawn(default);
            }

            return last;
        });

        using TestWorld world = new TestWorld();
        world.Spawn(new Position());

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(positions.Has(entity), Is.False);
        Assert.Throws<ArgumentException>(() => _ = positions[entity]);
    }

    // Takes about two minutes in Release: the index is reused until its version runs out, through about two billion updates that each destroy the
    // entity and spawn the next one.
    [Test, Explicit]
    public void IndexIsRetiredWhenItsVersionRunsOut()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Spawn(new Position());
        Entity entity = first;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.EntityDestroyer(), destroyer => destroyer.Destroy(entity)));
        // The lookup names a component, so a sync point before this system frees the destroyed index for the spawn.
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Position>>(), builder.ReadOnlyComponentLookup<Position>(), (spawner, _) => entity = spawner.Spawn(default)));
        for (int version = 1; version < int.MaxValue; version++)
        {
            world.Scheduler.Update();
        }

        Assert.That(entity.ToString(), Is.EqualTo($"Entity {{ Index=0, Version={int.MaxValue} }}"));

        world.Scheduler.Update();

        Assert.That(entity.ToString(), Does.StartWith("Entity { Index=1,"));
        Assert.That(world.Lookup<Position>().Has(first), Is.False);
    }

    [Test]
    public void LookupUnsafeGetReturnsTheReferenceTheIndexerReturns()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(1, 2), new Velocity(3, 4));
        bool sameInSystem = false;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Position>(), builder.ComponentLookup<Velocity>(), (positions, velocities) =>
        {
            velocities.UnsafeGet(entity).X = 5;
            sameInSystem = Unsafe.AreSame(ref positions.UnsafeGet(entity), ref positions[entity]);
        }));

        world.Scheduler.Update();

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(sameInSystem, Is.True);
        Assert.That(Unsafe.AreSame(ref Unsafe.AsRef(in positions.UnsafeGet(entity)), ref Unsafe.AsRef(in positions[entity])), Is.True);
        Assert.That(world.Lookup<Velocity>()[entity], Is.EqualTo(new Velocity(5, 4)));
    }

    [Test]
    public void EntitiesAreEqualOnlyWithTheSameIndexAndVersion()
    {
        using TestWorld world = new TestWorld();
        Entity destroyed = world.Spawn(new Position());
        world.Destroy(destroyed);
        Entity reused = world.Spawn(new Position());
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
        using TestWorld world = new TestWorld();
        Entity destroyed = world.Spawn(new Position());
        world.Destroy(destroyed);

        Entity reused = world.Spawn(new Position());

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(positions.Has(destroyed), Is.False);
        Assert.That(positions.Has(reused), Is.True);
        Assert.That(reused, Is.Not.EqualTo(destroyed));
    }

    [Test]
    public void WritingADeadEntityIsSkippedAndReadingItThrows()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position());
        world.Destroy(entity);
        world.Scheduler.Add(builder => ActionSystem.Of(builder.EntityDestroyer(), builder.ComponentAdder<Velocity>(), builder.ComponentRemover<Position>(), (destroyer, velocities, positions) =>
        {
            destroyer.Destroy(entity);
            velocities.Add(entity, new Velocity());
            positions.Remove(entity);
        }));

        Assert.DoesNotThrow(world.Scheduler.Update);

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(positions.Has(entity), Is.False);
        Assert.That(world.Lookup<Velocity>().Has(entity), Is.False);
        Assert.Throws<ArgumentException>(() => _ = positions[entity]);
    }

    [Test]
    public void LookupIndexerReturnsAReferenceToTheStoredComponent()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(1, 2));

        world.Scheduler.RunOnce(builder => builder.ComponentLookup<Position>(), positions => positions[entity].X = 5);

        Assert.That(world.Lookup<Position>()[entity], Is.EqualTo(new Position(5, 2)));
    }

    // A column replaces its array each time the archetype grows, so a lookup, which outlives every growth, must not keep reading an earlier one.
    [Test]
    public void LookupReadsEveryRowAfterTheArchetypeGrowsManyTimes()
    {
        using TestWorld world = new TestWorld();
        List<Entity> entities = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Spawner<Spawner<Position>>(), spawner => entities.Add(spawner.Spawn(new Position(entities.Count, 0)))));

        for (int i = 0; i < 20_000; i++)
        {
            world.Scheduler.Update();
        }

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        for (int i = 0; i < entities.Count; i++)
        {
            Assert.That(positions[entities[i]].X, Is.EqualTo(i));
            Assert.That(positions.UnsafeGet(entities[i]).X, Is.EqualTo(i));
        }
    }

    [Test]
    public void AddingAndRemovingKeepsOtherComponents()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(1, 2));
        world.AddComponent(entity, new Velocity(3, 4));
        world.AddComponent(entity, new Name("dude"));

        world.RemoveComponent<Velocity>(entity);

        Assert.That(world.Lookup<Velocity>().Has(entity), Is.False);
        Assert.That(world.Lookup<Position>()[entity], Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Lookup<Name>()[entity], Is.EqualTo(new Name("dude")));
    }

    [Test]
    public void ReadingAnAbsentComponentThrows()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position());

        ReadOnlyComponentLookup<Velocity> velocities = world.Lookup<Velocity>();
        Assert.That(velocities.Has(entity), Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = velocities[entity]);
    }

    // No other test uses this component, so its id is allocated after the test creates its archetype.
    private struct FirstUsedLate;

    [Test]
    public void ComponentFirstUsedAfterTheArchetypeIsAbsentAndCanBeAdded()
    {
        using TestWorld world = new TestWorld(services => services.AddReadOnlyComponentLookup<FirstUsedLate>());
        Entity entity = world.Spawn(new Position(1, 2));
        ReadOnlyComponentLookup<FirstUsedLate> late = world.Lookup<FirstUsedLate>();
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();

        Assert.That(late.Has(entity), Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = late[entity]);

        world.AddComponent(entity, new FirstUsedLate());

        Assert.That(late.Has(entity), Is.True);
        Assert.That(positions[entity], Is.EqualTo(new Position(1, 2)));
        Assert.That(late.UnsafeGet(entity), Is.EqualTo(default(FirstUsedLate)));
        Assert.That(positions.UnsafeGet(entity), Is.EqualTo(new Position(1, 2)));
    }

    private record struct SpacedFirst(int Value);

    private record struct SpacedGap(int Value);

    private record struct SpacedSecond(int Value);

    private sealed class SpacedSpawner : Spawner<SpacedFirst, SpacedSecond, Position>, IWith<Frozen>;

    // The archetype lacks the id between its two data columns and has a tag, so each column's array sits at its own component id, which the
    // archetype must refresh as it grows.
    [Test]
    public void EachColumnReadsItsOwnRowsWhenComponentIdsHaveGapsAndTags()
    {
        using TestWorld world = new TestWorld(services =>
        {
            services.AddReadOnlyComponentLookup<SpacedFirst>();
            services.AddReadOnlyComponentLookup<SpacedGap>();
            services.AddReadOnlyComponentLookup<SpacedSecond>();
        });
        List<Entity> entities = [];
        // The builder takes the id of each component it is first given, so SpacedGap's id falls between those of SpacedFirst and SpacedSecond.
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyComponentLookup<SpacedFirst>(), builder.ReadOnlyComponentLookup<SpacedGap>(), builder.Spawner<SpacedSpawner>(), (_, _, spawner) =>
        {
            int i = entities.Count;
            entities.Add(spawner.Spawn(new SpacedFirst(i), new SpacedSecond(-i), new Position(i, 0)));
        }));

        for (int i = 0; i < 5000; i++)
        {
            world.Scheduler.Update();
        }

        ReadOnlyComponentLookup<SpacedFirst> firsts = world.Lookup<SpacedFirst>();
        ReadOnlyComponentLookup<SpacedGap> gaps = world.Lookup<SpacedGap>();
        ReadOnlyComponentLookup<Frozen> frozen = world.Lookup<Frozen>();
        ReadOnlyComponentLookup<SpacedSecond> seconds = world.Lookup<SpacedSecond>();
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        for (int i = 0; i < entities.Count; i++)
        {
            Assert.That(gaps.Has(entities[i]), Is.False);
            Assert.That(frozen.Has(entities[i]), Is.True);
            Assert.That(firsts[entities[i]].Value, Is.EqualTo(i));
            Assert.That(seconds[entities[i]].Value, Is.EqualTo(-i));
            Assert.That(firsts.UnsafeGet(entities[i]).Value, Is.EqualTo(i));
            Assert.That(seconds.UnsafeGet(entities[i]).Value, Is.EqualTo(-i));
            Assert.That(positions.UnsafeGet(entities[i]).X, Is.EqualTo(i));
        }
    }

    [Test]
    public void MovingTheLastRowOfAnArchetypeKeepsEveryRecord()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Spawn(new Position(1, 2));
        Entity last = world.Spawn(new Position(3, 4));
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        ReadOnlyComponentLookup<Velocity> velocities = world.Lookup<Velocity>();

        world.AddComponent(last, new Velocity(5, 6));

        Assert.That(positions[first], Is.EqualTo(new Position(1, 2)));
        Assert.That(positions[last], Is.EqualTo(new Position(3, 4)));
        Assert.That(velocities[last], Is.EqualTo(new Velocity(5, 6)));

        world.RemoveComponent<Velocity>(last);

        Assert.That(positions[first], Is.EqualTo(new Position(1, 2)));
        Assert.That(positions[last], Is.EqualTo(new Position(3, 4)));
        Assert.That(velocities.Has(last), Is.False);
    }

    [Test]
    public void RemovingAReferenceComponentReleasesOnlyTheRemovedValue()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Spawn(new Position());
        WeakReference removed = AddUniqueName(world, first, 'a');
        Entity last = world.Spawn(new Position(), new Name(new string('b', 100)));

        world.RemoveComponent<Name>(first);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.That(removed.IsAlive, Is.False);
        Assert.That(world.Lookup<Name>()[last], Is.EqualTo(new Name(new string('b', 100))));
    }

    // No other test uses this component, so its id is past the edge arrays of every archetype the test creates.
    private struct AddedAndRemovedLate;

    [Test]
    public void ComponentFirstUsedAfterTheArchetypeCanBeAddedAndRemovedAgain()
    {
        using TestWorld world = new TestWorld(services => services.AddReadOnlyComponentLookup<AddedAndRemovedLate>());
        Entity entity = world.Spawn(new Position(1, 2));

        world.AddComponent(entity, new AddedAndRemovedLate());
        world.RemoveComponent<AddedAndRemovedLate>(entity);
        world.AddComponent(entity, new AddedAndRemovedLate());

        Assert.That(world.Lookup<AddedAndRemovedLate>().Has(entity), Is.True);
        Assert.That(world.Lookup<Position>()[entity], Is.EqualTo(new Position(1, 2)));
    }

    [Test]
    public void EntitiesKeepTheirComponentsWhenAnotherLeavesTheArchetype()
    {
        using TestWorld world = new TestWorld();
        Entity[] entities = world.Scheduler.RunOnce(builder => builder.Spawner<Spawner<Position, Velocity>>(), spawner =>
            Enumerable.Range(0, 4).Select(i => spawner.Spawn(new Position(i, i), new Velocity())).ToArray());

        world.Destroy(entities[0]);
        world.RemoveComponent<Position>(entities[1]);

        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        Assert.That(positions[entities[2]], Is.EqualTo(new Position(2, 2)));
        Assert.That(positions[entities[3]], Is.EqualTo(new Position(3, 3)));
        Assert.That(positions.Has(entities[1]), Is.False);
        Assert.That(world.Lookup<Velocity>().Has(entities[1]), Is.True);
    }

    // Creates the string here, so nothing but the world references it once this returns and the scheduler drops the system that added it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddUniqueName(TestWorld world, Entity entity, char character)
    {
        string value = new string(character, 100);
        world.AddComponent(entity, new Name(value));
        return new WeakReference(value);
    }
}
