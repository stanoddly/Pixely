using Pixely.DependencyInjection;

namespace Pixely.Ecs.Tests;

// A unique is one instance per type in a world, such as terrain. Systems write it through Unique<T>, and everyone reads it through its read-only type.
public class UniqueTests
{
    public interface IReadOnlyTerrain
    {
        int Height(int x);
    }

    public sealed class Terrain : IReadOnlyTerrain
    {
        private readonly int[] _heights = new int[4];

        public int Height(int x) => _heights[x];

        public void Raise(int x) => _heights[x]++;
    }

    public sealed class Digging(Unique<Terrain> terrain, ReadOnlyUnique<IReadOnlyTerrain> readTerrain) : ISystem
    {
        public List<int> SeenHeights { get; } = [];

        public void Update()
        {
            terrain.Value.Raise(1);
            SeenHeights.Add(readTerrain.Value.Height(1));
        }
    }

    [Test]
    public void SystemWritesTheUniqueThatCodeOutsideSystemsReads()
    {
        Terrain terrain = new Terrain();
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique<Terrain, IReadOnlyTerrain>(terrain);
        services.AddSystem<Digging>();
        using ServiceProvider provider = services.BuildServiceProvider();
        ReadOnlyUnique<IReadOnlyTerrain> readTerrain = provider.GetRequiredService<ReadOnlyUnique<IReadOnlyTerrain>>();

        provider.GetRequiredService<SystemScheduler>().Update();
        provider.GetRequiredService<SystemScheduler>().Update();

        Assert.That(readTerrain.Value, Is.SameAs(terrain));
        Assert.That(readTerrain.Value.Height(1), Is.EqualTo(2));
        Assert.That(provider.GetRequiredService<Digging>().SeenHeights, Is.EqualTo(new[] { 1, 2 }));
    }

    // Only systems reach the type that writes, so the provider has neither the class nor a read-only unique of it.
    [Test]
    public void AddUniqueRegistersOnlyTheReadOnlyUnique()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain());
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<Terrain>(), Is.Null);
        Assert.That(provider.GetService<IReadOnlyTerrain>(), Is.Null);
        Assert.That(provider.GetService<Unique<Terrain>>(), Is.Null);
        Assert.That(provider.GetService<ReadOnlyUnique<Terrain>>(), Is.Null);
    }

    [Test]
    public void ReadOnlyUniqueOfTheWritableTypeThrows()
    {
        using TestWorld world = new TestWorld(services => services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain()));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => world.Scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyUnique<Terrain>(), _ => { })));

        Assert.That(exception.Message, Does.StartWith($"The world has no unique whose read-only type is {nameof(Terrain)}."));
    }

    [Test]
    public void SystemsUniqueUsedOutsideItsUpdateThrows()
    {
        using TestWorld world = new TestWorld(services => services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain()));
        (Unique<Terrain> terrain, ReadOnlyUnique<IReadOnlyTerrain> readTerrain) = world.Scheduler.RunOnce(builder => (builder.Unique<Terrain>(), builder.ReadOnlyUnique<IReadOnlyTerrain>()), uniques => uniques);

        Assert.Throws<InvalidOperationException>(() => _ = terrain.Value);
        Assert.Throws<InvalidOperationException>(() => _ = readTerrain.Value);
    }

    [Test]
    public void UniqueMissingFromTheWorldThrowsWhenTheSystemIsAdded()
    {
        using TestWorld world = new TestWorld();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => world.Scheduler.Add(builder => ActionSystem.Of(builder.Unique<Terrain>(), _ => { })));

        Assert.That(exception.Message, Does.StartWith($"The world has no unique {nameof(Terrain)}."));
    }

    // Code outside systems could write a unique whose read-only type is the class itself.
    [Test]
    public void UniqueWhoseReadOnlyTypeIsItselfThrows()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();

        Assert.Throws<ArgumentException>(() => services.AddUnique<Terrain, Terrain>(new Terrain()));
    }

    [Test]
    public void SecondUniqueOfATypeOrOfAReadOnlyTypeThrows()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain());

        Assert.Throws<InvalidOperationException>(() => services.AddUnique<Terrain, object>(new Terrain()));
        Assert.Throws<InvalidOperationException>(() => services.AddUnique<OtherTerrain, IReadOnlyTerrain>(new OtherTerrain()));
    }

    [Test]
    public void AddUniqueInACollectionWithoutAddEcsThrows()
    {
        ServiceCollection services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain()));
    }

    // The child's unique would outlive the child in its parent's world.
    [Test]
    public void AddUniqueInAChildOfACollectionWithAWorldThrows()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();

        Assert.Throws<InvalidOperationException>(() => stage.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain()));
    }

    // A child collection without AddEcs adds its systems to its parent's world, so they reach the parent's uniques.
    [Test]
    public void SystemOfAChildCollectionWritesItsParentsUnique()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain());
        using ServiceProvider root = services.BuildServiceProvider();
        ServiceCollection stage = root.CreateServiceCollection();
        stage.AddSystem<Digging>();
        using ServiceProvider child = stage.BuildServiceProvider();

        root.GetRequiredService<SystemScheduler>().Update();

        Assert.That(child.GetRequiredService<ReadOnlyUnique<IReadOnlyTerrain>>().Value.Height(1), Is.EqualTo(1));
    }

    [Test]
    public void EachWorldHasItsOwnUnique()
    {
        Terrain first = new Terrain();
        Terrain second = new Terrain();
        using TestWorld firstWorld = new TestWorld(services => services.AddUnique<Terrain, IReadOnlyTerrain>(first));
        using TestWorld secondWorld = new TestWorld(services => services.AddUnique<Terrain, IReadOnlyTerrain>(second));

        firstWorld.Scheduler.RunOnce(builder => builder.Unique<Terrain>(), terrain => terrain.Value.Raise(1));

        Assert.That(firstWorld.Get<ReadOnlyUnique<IReadOnlyTerrain>>().Value, Is.SameAs(first));
        Assert.That(secondWorld.Get<ReadOnlyUnique<IReadOnlyTerrain>>().Value, Is.SameAs(second));
        Assert.That(second.Height(1), Is.Zero);
    }

    public sealed class OtherTerrain : IReadOnlyTerrain
    {
        public int Height(int x) => 0;
    }

    // Every system that uses it writes it, because a roll changes what the next roll gives.
    public sealed class Dice
    {
        private int _next;

        public int Roll() => ++_next;
    }

    [Test]
    public void SystemWritesAUniqueWithoutAReadOnlyType()
    {
        Dice dice = new Dice();
        using TestWorld world = new TestWorld(services => services.AddUnique(dice));

        int rolled = world.Scheduler.RunOnce(builder => builder.Unique<Dice>(), unique => unique.Value.Roll());

        Assert.That(rolled, Is.EqualTo(1));
        Assert.That(dice.Roll(), Is.EqualTo(2));
    }

    // Nothing outside systems reaches a unique without a read-only type.
    [Test]
    public void AddUniqueWithoutAReadOnlyTypeRegistersNothing()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique(new Dice());
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<Dice>(), Is.Null);
        Assert.That(provider.GetService<Unique<Dice>>(), Is.Null);
        Assert.That(provider.GetService<ReadOnlyUnique<Dice>>(), Is.Null);
    }

    [Test]
    public void SecondUniqueOfAClassWithoutAReadOnlyTypeThrows()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddEcs();
        services.AddUnique(new Dice());
        services.AddUnique<Terrain, IReadOnlyTerrain>(new Terrain());

        Assert.Throws<InvalidOperationException>(() => services.AddUnique(new Dice()));
        Assert.Throws<InvalidOperationException>(() => services.AddUnique(new Terrain()));
    }

    // AddUnique(terrain) compiles without the read-only type, so the message names the call to fix.
    [Test]
    public void ReadOnlyUniqueOfAUniqueAddedWithoutAReadOnlyTypeNamesTheCall()
    {
        using TestWorld world = new TestWorld(services => services.AddUnique(new Terrain()));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => world.Scheduler.Add(builder => ActionSystem.Of(builder.ReadOnlyUnique<IReadOnlyTerrain>(), _ => { })));

        Assert.That(exception.Message, Does.Contain($"Add it with AddUnique<{nameof(Terrain)}, {nameof(IReadOnlyTerrain)}> instead."));
    }

    [Test]
    public void AddUniqueWithoutAReadOnlyTypeInACollectionWithoutAddEcsThrows()
    {
        ServiceCollection services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddUnique(new Dice()));
    }
}
