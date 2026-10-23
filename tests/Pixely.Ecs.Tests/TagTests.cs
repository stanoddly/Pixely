namespace Pixely.Ecs.Tests;

public class TagTests
{
    private sealed class ActionSystem(Action update) : ISystem
    {
        public void Update() => update();
    }

    // Hand-written, to ask for the column of its tag field.
    private struct TagColumnBlueprint : IBlueprint<TagColumnBlueprint>
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

        int plainRows = FirstChunkCount<UnfrozenPositions>(world, chunk => chunk.Positions.Length);
        int frozenRows = FirstChunkCount<FrozenPositions>(world, chunk => chunk.Positions.Length);
        int playerRows = FirstChunkCount<PlayerPositions>(world, chunk => chunk.Positions.Length);

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
        ComponentAdder<Frozen> adder = null!;
        ComponentRemover<Frozen> remover = null!;
        bool remove = false;
        scheduler.Add(builder =>
        {
            adder = builder.ComponentAdder<Frozen>();
            remover = builder.ComponentRemover<Frozen>();
            return new ActionSystem(() =>
            {
                if (remove)
                {
                    remover.Remove(entity);
                }
                else
                {
                    adder.Add(entity, new Frozen());
                }
            });
        });

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
        Spawner<FrozenBlueprint> frozen = null!;
        Spawner<MarkerBlueprint> markers = null!;
        scheduler.Add(builder =>
        {
            frozen = builder.Spawner<FrozenBlueprint>();
            markers = builder.Spawner<MarkerBlueprint>();
            return new ActionSystem(() =>
            {
                withPosition = frozen.Spawn(new FrozenBlueprint { Position = new Position(1, 2) });
                onlyTag = markers.Spawn(new MarkerBlueprint());
            });
        });

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
        Spawner<TagColumnBlueprint> spawner = null!;
        scheduler.Add(builder =>
        {
            spawner = builder.Spawner<TagColumnBlueprint>();
            return new ActionSystem(() => spawner.Spawn(new TagColumnBlueprint()));
        });

        Assert.Throws<InvalidOperationException>(scheduler.Update);
    }

    [Test]
    public void ReadingTheSpanOfATagThrows()
    {
        World world = new World();
        Entity entity = world.Create();
        world.Add(entity, new Frozen());

        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (FrozenSpans _ in world.Query<FrozenSpans>())
            {
            }
        });
    }

    private delegate int RowsSelector<TQuery>(TQuery chunk) where TQuery : allows ref struct;

    private static int FirstChunkCount<TQuery>(World world, RowsSelector<TQuery> rows) where TQuery : IQuery<TQuery>, allows ref struct
    {
        foreach (TQuery chunk in world.Query<TQuery>())
        {
            return rows(chunk);
        }

        return 0;
    }
}
