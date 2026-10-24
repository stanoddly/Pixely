namespace Pixely.Ecs.Tests;

public class TagTests
{
    // Hand-written, to ask for the column of its tag field.
    internal struct TagColumnBlueprint : IBlueprint<TagColumnBlueprint>
    {
        public static void Describe(BlueprintDescription description) => description.Add<Frozen>();

        public static void Write(ReadOnlySpan<TagColumnBlueprint> blueprints, BlueprintColumns columns) => columns.Get<Frozen>(0);
    }

    // A tag adds no bytes to a row, so its chunks hold as many rows as without it, unlike an empty struct that is not a tag.
    [Test]
    public void TagTakesNoRowSpace()
    {
        World world = new World();
        for (int i = 0; i < 2000; i++)
        {
            Entity plain = world.Create();
            world.Add(plain, new Position(i, 0));
            Entity frozen = world.Create();
            world.Add(frozen, new Position(i, 0));
            world.Add(frozen, new Frozen());
            Entity player = world.Create();
            world.Add(player, new Position(i, 0));
            world.Add(player, new Player());
        }

        int plainRows = FirstChunkCount(world.Query<UnfrozenPositions>());
        int frozenRows = FirstChunkCount(world.Query<FrozenPositions>());
        int playerRows = FirstChunkCount(world.Query<PlayerPositions>());

        Assert.That(frozenRows, Is.EqualTo(plainRows));
        Assert.That(playerRows, Is.LessThan(plainRows));
    }

    [Test]
    public void AddingAndRemovingATagAcrossChunksKeepsEveryValue()
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
    public void BlueprintsSpawnTheirTags()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        Entity withPosition = default;
        Entity onlyTag = default;
        scheduler.Add(new ActionSystem<Spawner<FrozenBlueprint>, Spawner<MarkerBlueprint>>((frozen, markers) =>
        {
            withPosition = frozen.Spawn(new FrozenBlueprint { Position = new Position(1, 2) });
            onlyTag = markers.Spawn(new MarkerBlueprint());
        }));

        scheduler.Update();

        Assert.That(world.Has<Frozen>(withPosition), Is.True);
        Assert.That(world.Get<Position>(withPosition), Is.EqualTo(new Position(1, 2)));
        Assert.That(world.Has<Frozen>(onlyTag), Is.True);
        Assert.That(world.Has<Position>(onlyTag), Is.False);
    }

    [Test]
    public void BlueprintColumnOfATagThrows()
    {
        World world = new World();
        SystemScheduler scheduler = new SystemScheduler(world);
        scheduler.Add(new ActionSystem<Spawner<TagColumnBlueprint>>(spawner => spawner.Spawn(new TagColumnBlueprint())));

        Assert.Throws<InvalidOperationException>(scheduler.Update);
    }

    private static int FirstChunkCount<T1>(Query<T1> query) where T1 : allows ref struct
    {
        foreach (Columns<T1> columns in query)
        {
            return columns.Count;
        }

        return 0;
    }

    private static int FirstChunkCount<T1, T2>(Query<T1, T2> query) where T1 : allows ref struct where T2 : allows ref struct
    {
        foreach (Columns<T1, T2> columns in query)
        {
            return columns.Count;
        }

        return 0;
    }
}
