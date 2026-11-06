namespace Pixely.Ecs.Tests;

public class SystemOrderTests
{
    internal sealed class Early(List<string> log) : ISystem
    {
        public void Update() => log.Add(nameof(Early));
    }

    internal sealed class Plain(List<string> log, string name) : ISystem
    {
        public void Update() => log.Add(name);
    }

    internal sealed class Late(List<string> log) : ISystem, IAfter<Early>
    {
        public void Update() => log.Add(nameof(Late));
    }

    internal sealed class Opener(List<string> log) : ISystem, IBefore<Early>
    {
        public void Update() => log.Add(nameof(Opener));
    }

    internal sealed class Between(List<string> log) : ISystem, IAfter<Early>, IBefore<Late>
    {
        public void Update() => log.Add(nameof(Between));
    }

    internal sealed class Chicken : ISystem, IAfter<Egg>
    {
        public void Update()
        {
        }
    }

    internal sealed class Egg : ISystem, IAfter<Chicken>
    {
        public void Update()
        {
        }
    }

    internal sealed class LateCorpseCounter(Corpses corpses) : ISystem, IAfter<SystemSchedulerTests.Killer>
    {
        public int Count { get; private set; }

        public void Update()
        {
            Count = 0;
            foreach (Columns<ReadOnlySpan<Entity>> columns in corpses)
            {
                Count += columns.Count;
            }
        }
    }

    [Test]
    public void SystemRunsAfterTheSystemsOfItsIAfter()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Late(log));
        scheduler.Add(new Early(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { nameof(Early), nameof(Late) }));
    }

    [Test]
    public void SystemRunsBeforeTheSystemsOfItsIBefore()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Early(log));
        scheduler.Add(new Opener(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { nameof(Opener), nameof(Early) }));
    }

    [Test]
    public void SystemWithBothRunsBetweenThem()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Late(log));
        scheduler.Add(new Between(log));
        scheduler.Add(new Early(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { nameof(Early), nameof(Between), nameof(Late) }));
    }

    // Each step runs the earliest added system that may run, so a system without a declared order is not moved past another.
    [Test]
    public void SystemsWithoutADeclaredOrderKeepTheOrderTheyWereAdded()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Plain(log, "first"));
        scheduler.Add(new Late(log));
        scheduler.Add(new Plain(log, "second"));
        scheduler.Add(new Early(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { "first", "second", nameof(Early), nameof(Late) }));
    }

    [Test]
    public void SystemRunsAfterEveryInstanceOfItsTarget()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Late(log));
        scheduler.Add(new Early(log));
        scheduler.Add(new Early(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { nameof(Early), nameof(Early), nameof(Late) }));
    }

    [Test]
    public void TargetThatNoSystemHasIsIgnored()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Late(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { nameof(Late) }));
    }

    [Test]
    public void SystemAddedLaterIsOrderedFromTheNextUpdate()
    {
        List<string> log = [];
        SystemScheduler scheduler = new SystemScheduler(new World());
        scheduler.Add(new Late(log));
        scheduler.Update();
        log.Clear();
        scheduler.Add(new Early(log));

        scheduler.Update();

        Assert.That(log, Is.EqualTo(new[] { nameof(Early), nameof(Late) }));
    }

    [Test]
    public void CycleThrowsNamingItsSystemsUntilOneIsRemoved()
    {
        SystemScheduler scheduler = new SystemScheduler(new World());
        Chicken chicken = scheduler.Add(new Chicken());
        scheduler.Add(new Egg());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(scheduler.Update)!;
        Assert.That(exception.Message, Does.Contain($"{nameof(Egg)} -> {nameof(Chicken)} -> {nameof(Egg)}"));
        Assert.Throws<InvalidOperationException>(scheduler.Update);

        scheduler.Remove(chicken);
        Assert.DoesNotThrow(scheduler.Update);
    }

    // The sync points follow the declared order, so a system declared after the one that changes a component it names sees the change.
    [Test]
    public void SystemDeclaredAfterAChangeSeesItInTheSameUpdate()
    {
        using TestWorld world = new TestWorld();
        world.Spawn(new Position(), new Velocity());
        LateCorpseCounter counter = world.Scheduler.Add(builder => new LateCorpseCounter(builder.Query<Corpses>()));
        world.Scheduler.Add(builder => new SystemSchedulerTests.Killer(builder.Query<Movement>(), builder.ComponentAdder<Dead>()));

        world.Scheduler.Update();

        Assert.That(counter.Count, Is.EqualTo(1));
    }
}
