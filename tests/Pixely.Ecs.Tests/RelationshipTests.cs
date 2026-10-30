namespace Pixely.Ecs.Tests;

// Every entity here has a Name, so the lookup of Name tells whether it is alive.
public class RelationshipTests
{
    [Test]
    public void SourcesOfATargetFollowAddsAndRemoves()
    {
        using TestWorld world = new TestWorld();
        Entity box = world.Spawn(new Name("box"));
        Entity first = world.Spawn(new Name("first"));
        Entity second = world.Spawn(new Name("second"));

        world.AddComponent(first, new InsideOf(box));
        world.AddComponent(second, new InsideOf(box));
        Assert.That(world.Sources<InsideOf>(box), Is.EquivalentTo(new[] { first, second }));

        world.RemoveComponent<InsideOf>(first);
        Assert.That(world.Sources<InsideOf>(box), Is.EqualTo(new[] { second }));
        Assert.That(world.Sources<InsideOf>(first), Is.Empty);
    }

    [Test]
    public void AddingTheRelationshipAgainMovesTheSourceToItsNewTarget()
    {
        using TestWorld world = new TestWorld();
        Entity box = world.Spawn(new Name("box"));
        Entity bag = world.Spawn(new Name("bag"));
        Entity item = world.Spawn(new Name("item"), new InsideOf(box));

        world.AddComponent(item, new InsideOf(bag));

        Assert.That(world.Sources<InsideOf>(box), Is.Empty);
        Assert.That(world.Sources<InsideOf>(bag), Is.EqualTo(new[] { item }));
        Assert.That(world.Lookup<InsideOf>()[item], Is.EqualTo(new InsideOf(bag)));
    }

    // A target spawned earlier in the same system, or earlier in the same run of spawns, is alive when its sources are indexed.
    [Test]
    public void SpawnedSourcesAreIndexedWithTargetsSpawnedBeforeThem()
    {
        using TestWorld world = new TestWorld();

        (Entity box, Entity item, Entity body, Entity arm) = world.Scheduler.RunOnce(
            builder => (builder.Spawner<Spawner<Name>>(), builder.Spawner<Spawner<Name, InsideOf>>(), builder.Spawner<Spawner<Name, AttachedTo>>()), spawners =>
            {
                Entity box = spawners.Item1.Spawn(new Name("box"));
                Entity item = spawners.Item2.Spawn(new Name("item"), new InsideOf(box));
                Entity body = spawners.Item1.Spawn(new Name("body"));
                Entity arm = spawners.Item3.Spawn(new Name("arm"), new AttachedTo(body));
                return (box, item, body, arm);
            });

        Assert.That(world.Sources<InsideOf>(box), Is.EqualTo(new[] { item }));
        Assert.That(world.Sources<AttachedTo>(body), Is.EqualTo(new[] { arm }));
    }

    [Test]
    public void DestroyingATargetRemovesTheRelationshipFromItsSources()
    {
        using TestWorld world = new TestWorld();
        Entity box = world.Spawn(new Name("box"));
        Entity first = world.Spawn(new Name("first"), new InsideOf(box));
        Entity second = world.Spawn(new Name("second"), new InsideOf(box));

        world.Destroy(box);

        Assert.That(world.Lookup<Name>().Has(first), Is.True);
        Assert.That(world.Lookup<Name>().Has(second), Is.True);
        Assert.That(world.Lookup<InsideOf>().Has(first), Is.False);
        Assert.That(world.Lookup<InsideOf>().Has(second), Is.False);
        Assert.That(world.Sources<InsideOf>(box), Is.Empty);
    }

    [Test]
    public void DestroyingATargetDestroysTheSourcesDestroyedWithItAndTheirSourcesInTurn()
    {
        using TestWorld world = new TestWorld();
        Entity body = world.Spawn(new Name("body"));
        Entity arm = world.Spawn(new Name("arm"), new AttachedTo(body));
        Entity hand = world.Spawn(new Name("hand"), new AttachedTo(arm));
        Entity ring = world.Spawn(new Name("ring"), new InsideOf(hand));

        world.Destroy(body);

        Assert.That(world.Lookup<Name>().Has(arm), Is.False);
        Assert.That(world.Lookup<Name>().Has(hand), Is.False);
        Assert.That(world.Lookup<Name>().Has(ring), Is.True);
        Assert.That(world.Lookup<InsideOf>().Has(ring), Is.False);
    }

    [Test]
    public void DestroyingASourceRemovesItFromItsTargetsSources()
    {
        using TestWorld world = new TestWorld();
        Entity box = world.Spawn(new Name("box"));
        Entity first = world.Spawn(new Name("first"), new InsideOf(box));
        Entity second = world.Spawn(new Name("second"), new InsideOf(box));
        Entity third = world.Spawn(new Name("third"), new InsideOf(box));

        world.Destroy(first);

        Assert.That(world.Sources<InsideOf>(box), Is.EquivalentTo(new[] { second, third }));
    }

    [Test]
    public void RelationshipToATargetThatIsNotAliveIsRemovedOrDestroysItsSource()
    {
        using TestWorld world = new TestWorld();
        Entity gone = world.Spawn(new Name("gone"));
        world.Destroy(gone);
        Entity item = world.Spawn(new Name("item"));
        Entity part = world.Spawn(new Name("part"));

        world.AddComponent(item, new InsideOf(gone));
        world.AddComponent(part, new AttachedTo(gone));
        Entity spawnedItem = world.Spawn(new Name("spawned item"), new InsideOf(gone));
        Entity spawnedPart = world.Spawn(new Name("spawned part"), new AttachedTo(gone));

        Assert.That(world.Lookup<Name>().Has(item), Is.True);
        Assert.That(world.Lookup<InsideOf>().Has(item), Is.False);
        Assert.That(world.Lookup<Name>().Has(part), Is.False);
        Assert.That(world.Lookup<Name>().Has(spawnedItem), Is.True);
        Assert.That(world.Lookup<InsideOf>().Has(spawnedItem), Is.False);
        Assert.That(world.Lookup<Name>().Has(spawnedPart), Is.False);
    }

    // Even one destroyed with its target, which would otherwise destroy the entity.
    [Test]
    public void RelationshipToItsOwnSourceIsRemoved()
    {
        using TestWorld world = new TestWorld();
        Entity part = world.Spawn(new Name("part"));

        world.AddComponent(part, new AttachedTo(part));

        Assert.That(world.Lookup<Name>().Has(part), Is.True);
        Assert.That(world.Lookup<AttachedTo>().Has(part), Is.False);
    }

    [Test]
    public void DestroyingACycleOfRelationshipsDestroyedWithTheirTargetEnds()
    {
        using TestWorld world = new TestWorld();
        Entity first = world.Spawn(new Name("first"));
        Entity second = world.Spawn(new Name("second"), new AttachedTo(first));
        world.AddComponent(first, new AttachedTo(second));

        world.Destroy(first);

        Assert.That(world.Lookup<Name>().Has(first), Is.False);
        Assert.That(world.Lookup<Name>().Has(second), Is.False);
    }

    [Test]
    public void LaterSystemReadingSourcesSeesTheChangesOfAnEarlierOneInTheSameUpdate()
    {
        using TestWorld world = new TestWorld();
        Entity box = world.Spawn(new Name("box"));
        Entity item = world.Spawn(new Name("item"));
        Entity[] seen = [];
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<InsideOf>(), inside => inside.Add(item, new InsideOf(box))));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.RelationshipSources<InsideOf>(), sources => seen = sources[box].ToArray()));

        world.Scheduler.Update();

        Assert.That(seen, Is.EqualTo(new[] { item }));
    }

    // The added relationship destroys the part, so a later system with any query sees it gone, as after an EntityDestroyer.
    [Test]
    public void AddingARelationshipDestroyedWithItsTargetForcesASyncPointLikeADestroy()
    {
        using TestWorld world = new TestWorld();
        Entity gone = world.Spawn(new Name("gone"));
        world.Destroy(gone);
        Entity part = world.Spawn(new Name("part"));
        int names = -1;
        world.Scheduler.Add(builder => ActionSystem.Of(builder.ComponentAdder<AttachedTo>(), attached => attached.Add(part, new AttachedTo(gone))));
        world.Scheduler.Add(builder => ActionSystem.Of(builder.Query<Query<ReadOnlySpan<Name>>>(), query =>
        {
            names = 0;
            foreach (Columns<ReadOnlySpan<Name>> columns in query)
            {
                names += columns.Count;
            }
        }));

        world.Scheduler.Update();

        Assert.That(names, Is.Zero);
    }

    // The generator reports both, so the spans are registered and the system added here, where it does not look.
    [Test]
    public void QueryOrLookupWritingARelationshipThrows()
    {
        QueryTerms.RegisterSpan<InsideOf>();
        SystemScheduler scheduler = new SystemScheduler(new World());

        InvalidOperationException query = Assert.Throws<InvalidOperationException>(() => AddWritingQuery<InsideOf>(scheduler));
        InvalidOperationException lookup = Assert.Throws<InvalidOperationException>(() => scheduler.Add(builder => ActionSystem.Of(builder.ComponentLookup<Name, InsideOf>(), _ => { })));

        Assert.That(query.Message, Does.Contain("InsideOf, a relationship"));
        Assert.That(lookup.Message, Does.Contain("InsideOf, a relationship"));
    }

    // Generic, so the generator sees only T and does not report the query.
    private static void AddWritingQuery<T>(SystemScheduler scheduler) where T : struct => scheduler.Add(builder => ActionSystem.Of(builder.Query<Writable<T>>(), _ => { }));
}
