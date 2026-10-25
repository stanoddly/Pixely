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
        World world = new World();
        Entity[] entities = new Entity[1500];
        for (int i = 0; i < entities.Length; i++)
        {
            entities[i] = world.Create();
            world.Add(entities[i], new Position(i, -i));
            world.Add(entities[i], new Velocity(i, i));
        }

        for (int i = 0; i < entities.Length; i += 2)
        {
            world.Add(entities[i], new Frozen());
        }

        for (int i = 0; i < entities.Length; i += 4)
        {
            world.Remove<Frozen>(entities[i]);
        }

        for (int i = 0; i < entities.Length; i++)
        {
            Assert.That(world.Has<Frozen>(entities[i]), Is.EqualTo(i % 2 == 0 && i % 4 != 0));
            Assert.That(world.Get<Position>(entities[i]), Is.EqualTo(new Position(i, -i)));
            Assert.That(world.Get<Velocity>(entities[i]), Is.EqualTo(new Velocity(i, i)));
        }
    }

    [Test]
    public void EntityWithOnlyATagCanGainAndLoseIt()
    {
        World world = new World();
        Entity entity = world.Create();

        world.Add(entity, new Frozen());
        Assert.That(world.Has<Frozen>(entity), Is.True);
        Assert.Throws<InvalidOperationException>(() => world.Add(entity, new Frozen()));

        world.Remove<Frozen>(entity);
        Assert.That(world.Has<Frozen>(entity), Is.False);
        Assert.Throws<InvalidOperationException>(() => world.Remove<Frozen>(entity));
    }

    [Test]
    public void GetOfATagReturnsTheSharedValueAndGetOfAnAbsentTagThrows()
    {
        World world = new World();
        Entity frozen = world.Create();
        world.Add(frozen, new Frozen());
        Entity other = world.Create();

        Assert.That(world.Get<Frozen>(frozen), Is.EqualTo(default(Frozen)));
        Assert.Throws<InvalidOperationException>(() => world.Get<Frozen>(other));
    }

    [Test]
    public void UnsafeGetOfATagReturnsTheSharedValue()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Frozen());

        Assert.That(Unsafe.AreSame(ref world.UnsafeGet<Frozen>(entity), ref world.Get<Frozen>(entity)), Is.True);
    }

    [Test]
    public void WritersAddAPresentTagAndRemoveIt()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Frozen());
        SystemScheduler scheduler = new SystemScheduler(world);
        bool remove = false;
        scheduler.Add(new ActionSystem<ComponentAdder<Frozen>, ComponentRemover<Frozen>>((adder, remover) =>
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

        Assert.DoesNotThrow(scheduler.Update);
        Assert.That(world.Has<Frozen>(entity), Is.True);

        remove = true;
        scheduler.Update();
        Assert.That(world.Has<Frozen>(entity), Is.False);
    }

    [Test]
    public void SpawnerGivesItsEntitiesTheTagsOfIWith()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity spawned = default;
        scheduler.Add(new ActionSystem<FrozenSpawner>(frozen => spawned = frozen.Spawn(new Position(1, 2))));

        scheduler.Update();

        Assert.That(world.Has<Frozen>(spawned), Is.True);
        Assert.That(world.Get<Position>(spawned), Is.EqualTo(new Position(1, 2)));
    }

    [Test]
    public void SpawnerTakingATagOrNamingDataInIWithThrows()
    {
        World world = new World();

        Assert.Throws<InvalidOperationException>(() => world.EnsureCapacity<TagArgumentSpawner>(1));
        Assert.Throws<InvalidOperationException>(() => world.EnsureCapacity<DataInWithSpawner>(1));
    }
}
