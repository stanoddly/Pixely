using Pixely.DependencyInjection;

namespace Pixely.DependencyInjection.Tests;

public interface ITransientContract;

public sealed class TransientService : ITransientContract;

public sealed class AnotherTransientService : ITransientContract;

public sealed class TransientWithDependency
{
    public TransientWithDependency(SimpleService simple)
    {
        Simple = simple;
    }

    public SimpleService Simple { get; }
}

public sealed class SingletonWithTransient
{
    public SingletonWithTransient(TransientService transient)
    {
        Transient = transient;
    }

    public TransientService Transient { get; }
}

public sealed class TransientDisposable : IDisposable
{
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
        DisposeCallCount++;
    }
}

public interface ITransientDisposableContract;

public sealed class TransientDisposableImpl : ITransientDisposableContract, IDisposable
{
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
        DisposeCallCount++;
    }
}

public sealed class TransientFactory
{
    public TransientProduct CreateProduct(SimpleService simple)
    {
        return new TransientProduct(simple);
    }
}

public sealed class TransientProduct
{
    public TransientProduct(SimpleService simple)
    {
        Simple = simple;
    }

    public SimpleService Simple { get; }
}

public sealed class TransientServiceTests
{
    [Test]
    public void AddTransient_ReturnsNewInstanceEachResolution()
    {
        ServiceCollection collection = new();
        collection.AddTransient<TransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        TransientService first = provider.GetRequiredService<TransientService>();
        TransientService second = provider.GetRequiredService<TransientService>();

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void AddTransient_WithDependency_ResolvesSingletonDependency()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddTransient<TransientWithDependency>();

        ServiceProvider provider = collection.BuildServiceProvider();

        TransientWithDependency transient = provider.GetRequiredService<TransientWithDependency>();

        Assert.That(transient.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void AddTransient_WithServiceType_ResolvesImplementation()
    {
        ServiceCollection collection = new();
        collection.AddTransient<ITransientContract, TransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ITransientContract first = provider.GetRequiredService<ITransientContract>();
        ITransientContract second = provider.GetRequiredService<ITransientContract>();

        Assert.That(first, Is.InstanceOf<TransientService>());
        Assert.That(second, Is.InstanceOf<TransientService>());
        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void AddTransient_FactoryDelegate_ResolvesNewInstanceEachTime()
    {
        ServiceCollection collection = new();
        collection.AddTransient<TransientService>(() => new TransientService());

        ServiceProvider provider = collection.BuildServiceProvider();
        TransientService first = provider.GetRequiredService<TransientService>();
        TransientService second = provider.GetRequiredService<TransientService>();

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void AddTransient_FactoryReturnsNull_ContributesNoServicePerResolution()
    {
        int factoryCalls = 0;
        ServiceCollection collection = new();
        collection.AddTransient<ITransientContract>(
            (Func<ServiceProvider, ITransientContract?>)(_ =>
            {
                factoryCalls++;
                return null;
            }));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(factoryCalls, Is.Zero);
        Assert.That(provider.GetService<ITransientContract>(), Is.Null);
        Assert.That(provider.GetServices<ITransientContract>(), Is.Empty);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ITransientContract>());
        Assert.That(factoryCalls, Is.EqualTo(3));
    }

    [Test]
    public void GetRequiredService_LastNullTransientFactory_FallsBackToEarlierSingleton()
    {
        TransientService fallback = new();
        ServiceCollection collection = new();
        collection.AddSingleton<ITransientContract>(fallback);
        collection.AddTransient<ITransientContract>(
            (Func<ServiceProvider, ITransientContract?>)(_ => null));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ITransientContract>(), Is.SameAs(fallback));
        Assert.That(provider.GetServices<ITransientContract>(), Is.EqualTo(new[] { fallback }));
    }

    [Test]
    public void AddTransient_InstanceFactory_UsesFactoryServiceAndMethodDependencies()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<TransientFactory>();
        collection.AddTransient<TransientProduct, TransientFactory>();

        ServiceProvider provider = collection.BuildServiceProvider();

        TransientProduct first = provider.GetRequiredService<TransientProduct>();
        TransientProduct second = provider.GetRequiredService<TransientProduct>();

        Assert.That(first, Is.Not.SameAs(second));
        Assert.That(first.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
        Assert.That(second.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void GetRequiredService_LastRegistrationWins_WhenTransientOverridesSingleton()
    {
        ServiceCollection collection = new();
        TransientService singleton = new();
        collection.AddSingleton<ITransientContract>(singleton);
        collection.AddTransient<ITransientContract, AnotherTransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ITransientContract first = provider.GetRequiredService<ITransientContract>();
        ITransientContract second = provider.GetRequiredService<ITransientContract>();

        Assert.That(first, Is.InstanceOf<AnotherTransientService>());
        Assert.That(second, Is.InstanceOf<AnotherTransientService>());
        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void GetServices_IncludesSingletonsAndTransients_InRegistrationOrder()
    {
        ServiceCollection collection = new();
        TransientService singleton = new();
        collection.AddSingleton<ITransientContract>(singleton);
        collection.AddTransient<ITransientContract, AnotherTransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<ITransientContract> services = provider.GetServices<ITransientContract>();

        Assert.That(services, Has.Count.EqualTo(2));
        Assert.That(services[0], Is.SameAs(singleton));
        Assert.That(services[1], Is.InstanceOf<AnotherTransientService>());
    }

    [Test]
    public void GetServices_RecreatesTransientEntries_PerCall()
    {
        ServiceCollection collection = new();
        TransientService singleton = new();
        collection.AddSingleton<ITransientContract>(singleton);
        collection.AddTransient<ITransientContract, AnotherTransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<ITransientContract> first = provider.GetServices<ITransientContract>();
        IReadOnlyList<ITransientContract> second = provider.GetServices<ITransientContract>();

        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second[0], Is.SameAs(first[0]));
        Assert.That(second[1], Is.Not.SameAs(first[1]));
    }

    [Test]
    public void Singleton_CapturesTransientCreatedDuringBuild()
    {
        ServiceCollection collection = new();
        collection.AddTransient<TransientService>();
        collection.AddSingleton<SingletonWithTransient>();

        ServiceProvider provider = collection.BuildServiceProvider();

        SingletonWithTransient singleton = provider.GetRequiredService<SingletonWithTransient>();

        Assert.That(provider.GetRequiredService<SingletonWithTransient>(), Is.SameAs(singleton));
        Assert.That(provider.GetRequiredService<TransientService>(), Is.Not.SameAs(singleton.Transient));
    }

    [Test]
    public void Dispose_DisposesCreatedTransientDisposables()
    {
        ServiceCollection collection = new();
        collection.AddTransient<TransientDisposable>();
        ServiceProvider provider = collection.BuildServiceProvider();

        TransientDisposable first = provider.GetRequiredService<TransientDisposable>();
        TransientDisposable second = provider.GetRequiredService<TransientDisposable>();

        provider.Dispose();

        Assert.That(first.DisposeCallCount, Is.EqualTo(1));
        Assert.That(second.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public void Dispose_DisposesTransientFactoryResult_WhenServiceTypeIsNotDisposable()
    {
        TransientDisposableImpl instance = new();
        ServiceCollection collection = new();
        collection.AddTransient<ITransientDisposableContract>(_ => instance);
        ServiceProvider provider = collection.BuildServiceProvider();

        provider.GetRequiredService<ITransientDisposableContract>();

        provider.Dispose();

        Assert.That(instance.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ChildResolvingParentTransient_OwnsTransientDisposable()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddTransient<TransientDisposable>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        ServiceProvider child = childCollection.BuildServiceProvider();

        TransientDisposable disposable = child.GetRequiredService<TransientDisposable>();

        child.Dispose();

        Assert.That(disposable.DisposeCallCount, Is.EqualTo(1));

        parent.Dispose();

        Assert.That(disposable.DisposeCallCount, Is.EqualTo(1));
    }

    [Test]
    public void ChildGetServices_IncludesParentTransient_AndChildOwnsCreatedInstance()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddTransient<ITransientContract, TransientService>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddTransient<ITransientContract, AnotherTransientService>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        IReadOnlyList<ITransientContract> first = child.GetServices<ITransientContract>();
        IReadOnlyList<ITransientContract> second = child.GetServices<ITransientContract>();

        Assert.That(first, Has.Count.EqualTo(2));
        Assert.That(first[0], Is.InstanceOf<TransientService>());
        Assert.That(first[1], Is.InstanceOf<AnotherTransientService>());
        Assert.That(second[0], Is.Not.SameAs(first[0]));
        Assert.That(second[1], Is.Not.SameAs(first[1]));
    }

    [Test]
    public void AddAlias_ToTransientImplementation_ResolvesNewInstancePerAliasResolution()
    {
        ServiceCollection collection = new();
        collection.AddTransient<TransientService>();
        collection.AddAlias<ITransientContract, TransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ITransientContract first = provider.GetRequiredService<ITransientContract>();
        ITransientContract second = provider.GetRequiredService<ITransientContract>();

        Assert.That(first, Is.InstanceOf<TransientService>());
        Assert.That(second, Is.InstanceOf<TransientService>());
        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void AddAlias_ToAbsentTransientImplementation_ContributesNoService()
    {
        ServiceCollection collection = new();
        collection.AddTransient<TransientService>(
            (Func<ServiceProvider, TransientService?>)(_ => null));
        collection.AddAlias<ITransientContract, TransientService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetService<ITransientContract>(), Is.Null);
        Assert.That(provider.GetServices<ITransientContract>(), Is.Empty);
    }

    [Test]
    public void Callbacks_RunForTransientActivationAndDisposal()
    {
        List<string> calls = new();

        ServiceCollection collection = new();
        collection.OnActivated((_, type) => calls.Add($"activated:{type.Name}"));
        collection.OnDisposing((_, type) => calls.Add($"disposing:{type.Name}"));
        collection.AddTransient<TransientDisposable>();

        ServiceProvider provider = collection.BuildServiceProvider();

        provider.GetRequiredService<TransientDisposable>();
        provider.Dispose();

        Assert.That(calls, Is.EqualTo(new[]
        {
            $"activated:{nameof(TransientDisposable)}",
            $"disposing:{nameof(TransientDisposable)}"
        }));
    }

    [Test]
    public void Callbacks_RunForNonDisposableTransientDisposal()
    {
        List<object> disposing = new();

        ServiceCollection collection = new();
        collection.OnDisposing((instance, _) => disposing.Add(instance));
        collection.AddTransient<TransientService>(static _ => new TransientService());

        ServiceProvider provider = collection.BuildServiceProvider();

        TransientService first = provider.GetRequiredService<TransientService>();
        TransientService second = provider.GetRequiredService<TransientService>();
        provider.Dispose();

        Assert.That(disposing, Is.EqualTo(new object[] { second, first }));
    }

    [Test]
    public void ChildDispose_RemovesNonDisposableTransientFromParentRegistry()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddRegistry<ITransientContract>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();
        ServiceRegistry<ITransientContract> registry = parent.GetRequiredService<ServiceRegistry<ITransientContract>>();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddTransient<ITransientContract, TransientService>(static _ => new TransientService());
        ServiceProvider child = childCollection.BuildServiceProvider();
        ITransientContract transient = child.GetRequiredService<ITransientContract>();
        bool registered = registry.Contains(transient);

        child.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(registered, Is.True);
            Assert.That(registry, Is.Empty);
        });

        parent.Dispose();
    }
}
