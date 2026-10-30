using System.Runtime.CompilerServices;

namespace Pixely.Ecs.Tests;

public class TagTests
{
    // Wrong on purpose: tags go in IWith<...>, and IWith<...> names only tags. No system names them, so only the world checks them.
    public sealed class TagArgumentSpawner : Spawner<Position, Frozen>;

    public sealed class DataInWithSpawner : Spawner<Position>, IWith<Velocity>;

    [Test]
    public void AddingAndRemovingATagKeepsEveryValue()
    {
        using TestWorld world = new TestWorld();
        Entity[] entities = world.Scheduler.RunOnce(builder => builder.Spawner<MovingSpawner>(), spawner =>
            Enumerable.Range(0, 1500).Select(i => spawner.Spawn(new Position(i, -i), new Velocity(i, i))).ToArray());

        world.Scheduler.RunOnce(builder => builder.ComponentAdder<Frozen>(), frozen =>
        {
            for (int i = 0; i < entities.Length; i += 2)
            {
                frozen.Add(entities[i], new Frozen());
            }
        });

        world.Scheduler.RunOnce(builder => builder.ComponentRemover<Frozen>(), frozen =>
        {
            for (int i = 0; i < entities.Length; i += 4)
            {
                frozen.Remove(entities[i]);
            }
        });

        ReadOnlyComponentLookup<Frozen> frozen = world.Lookup<Frozen>();
        ReadOnlyComponentLookup<Position> positions = world.Lookup<Position>();
        ReadOnlyComponentLookup<Velocity> velocities = world.Lookup<Velocity>();
        for (int i = 0; i < entities.Length; i++)
        {
            Assert.That(frozen.Has(entities[i]), Is.EqualTo(i % 2 == 0 && i % 4 != 0));
            Assert.That(positions[entities[i]], Is.EqualTo(new Position(i, -i)));
            Assert.That(velocities[entities[i]], Is.EqualTo(new Velocity(i, i)));
        }
    }

    [Test]
    public void EntityCanGainAndLoseATag()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Spawn(new Position(1, 2));
        ReadOnlyComponentLookup<Frozen> frozen = world.Lookup<Frozen>();

        world.AddComponent(entity, new Frozen());
        Assert.That(frozen.Has(entity), Is.True);

        world.RemoveComponent<Frozen>(entity);
        Assert.That(frozen.Has(entity), Is.False);
        Assert.That(world.Lookup<Position>()[entity], Is.EqualTo(new Position(1, 2)));
    }

    [Test]
    public void LookupOfATagReturnsTheSharedValueAndThrowsForAnAbsentTag()
    {
        using TestWorld world = new TestWorld();
        Entity frozen = world.Scheduler.RunOnce(builder => builder.Spawner<FrozenSpawner>(), spawner => spawner.Spawn(default));
        Entity other = world.Spawn(new Position());

        ReadOnlyComponentLookup<Frozen> lookup = world.Lookup<Frozen>();
        Assert.That(lookup[frozen], Is.EqualTo(default(Frozen)));
        Assert.Throws<InvalidOperationException>(() => _ = lookup[other]);
    }

    [Test]
    public void LookupUnsafeGetOfATagReturnsTheSharedValue()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Scheduler.RunOnce(builder => builder.Spawner<FrozenSpawner>(), spawner => spawner.Spawn(default));

        ReadOnlyComponentLookup<Frozen> frozen = world.Lookup<Frozen>();
        Assert.That(Unsafe.AreSame(ref Unsafe.AsRef(in frozen.UnsafeGet(entity)), ref Unsafe.AsRef(in frozen[entity])), Is.True);
    }

    [Test]
    public void WritersAddAPresentTagAndRemoveIt()
    {
        using TestWorld world = new TestWorld();
        Entity entity = world.Scheduler.RunOnce(builder => builder.Spawner<FrozenSpawner>(), spawner => spawner.Spawn(default));
        bool remove = false;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<Frozen>(), builder.ComponentRemover<Frozen>(), (adder, remover) =>
        {
            if (remove)
            {
                remover.Remove(entity);
            }
            else
            {
                adder.Add(entity, new Frozen());
            }
        }));
        ReadOnlyComponentLookup<Frozen> frozen = world.Lookup<Frozen>();

        Assert.DoesNotThrow(world.Scheduler.Update);
        Assert.That(frozen.Has(entity), Is.True);

        remove = true;
        world.Scheduler.Update();
        Assert.That(frozen.Has(entity), Is.False);
    }

    [Test]
    public void SpawnerGivesItsEntitiesTheTagsOfIWith()
    {
        using TestWorld world = new TestWorld();

        Entity spawned = world.Scheduler.RunOnce(builder => builder.Spawner<FrozenSpawner>(), frozen => frozen.Spawn(new Position(1, 2)));

        Assert.That(world.Lookup<Frozen>().Has(spawned), Is.True);
        Assert.That(world.Lookup<Position>()[spawned], Is.EqualTo(new Position(1, 2)));
    }

    [Test]
    public void SpawnerTakingATagOrNamingDataInIWithThrows()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());

        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Spawner<TagArgumentSpawner>(), _ => { })));
        Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.Spawner<DataInWithSpawner>(), _ => { })));
    }
}
