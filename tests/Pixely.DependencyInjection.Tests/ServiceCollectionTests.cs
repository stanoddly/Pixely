using Pixely.DependencyInjection;

namespace Pixely.DependencyInjection.Tests;

public class SimpleService;

public class AnotherService;

public interface IMyService;

public class MyServiceImpl : IMyService;

public class AnotherServiceImpl : IMyService;

public sealed record FactoryCondition(bool Enabled);

public interface IConditionalService;

public sealed class ConditionalService : IConditionalService;

public class InternalConstructorMyServiceImpl : IMyService
{
    public SimpleService Simple { get; }

    internal InternalConstructorMyServiceImpl(SimpleService simple)
    {
        Simple = simple;
    }
}

public interface IUnrelated;

public class ServiceWithDependency
{
    public SimpleService Simple { get; }

    public ServiceWithDependency(SimpleService simple)
    {
        Simple = simple;
    }
}

public class ServiceWithTwoDependencies
{
    public SimpleService Simple { get; }
    public AnotherService Another { get; }

    public ServiceWithTwoDependencies(SimpleService simple, AnotherService another)
    {
        Simple = simple;
        Another = another;
    }
}

public class ServiceWithInternalConstructor
{
    public SimpleService Simple { get; }

    internal ServiceWithInternalConstructor(SimpleService simple)
    {
        Simple = simple;
    }
}

public class ServiceWithProtectedInternalConstructor
{
    public AnotherService Another { get; }

    protected internal ServiceWithProtectedInternalConstructor(AnotherService another)
    {
        Another = another;
    }
}

public class CircularServiceA
{
    public CircularServiceB B { get; }
    public CircularServiceA(CircularServiceB b) => B = b;
}

public class CircularServiceB
{
    public CircularServiceA A { get; }
    public CircularServiceB(CircularServiceA a) => A = a;
}

public class MultiConstructorService
{
    public MultiConstructorService() { }
    public MultiConstructorService(SimpleService simple) { }
}

public class DisposableService : IDisposable
{
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

public class ServiceCollectionTests
{
    // --- AddSingleton<T>() ---

    [Test]
    public void AddSingleton_ResolvesService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<SimpleService>(), Is.Not.Null);
    }

    [Test]
    public void AddSingleton_ReturnsSameInstance()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        SimpleService first = provider.GetRequiredService<SimpleService>();
        SimpleService second = provider.GetRequiredService<SimpleService>();

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void AddSingleton_WithDependency_ResolvesDependency()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<ServiceWithDependency>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithDependency service = provider.GetRequiredService<ServiceWithDependency>();

        Assert.That(service.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void AddSingleton_WithMultipleDependencies_ResolvesAll()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<AnotherService>();
        collection.AddSingleton<ServiceWithTwoDependencies>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithTwoDependencies service = provider.GetRequiredService<ServiceWithTwoDependencies>();

        Assert.That(service.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
        Assert.That(service.Another, Is.SameAs(provider.GetRequiredService<AnotherService>()));
    }

    [Test]
    public void AddSingleton_WithInternalConstructor_ResolvesService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<ServiceWithInternalConstructor>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithInternalConstructor service = provider.GetRequiredService<ServiceWithInternalConstructor>();

        Assert.That(service.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void AddSingleton_WithProtectedInternalConstructor_ResolvesService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<AnotherService>();
        collection.AddSingleton<ServiceWithProtectedInternalConstructor>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithProtectedInternalConstructor service = provider.GetRequiredService<ServiceWithProtectedInternalConstructor>();

        Assert.That(service.Another, Is.SameAs(provider.GetRequiredService<AnotherService>()));
    }

    [Test]
    public void AddSingleton_Duplicate_LastWins()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        // Second registration should not throw — last registration wins
        Assert.DoesNotThrow(() => collection.AddSingleton<SimpleService>());
    }

    // AddSingleton with multiple constructors is now a compile-time error (GK0002)

    [Test]
    public void AddSingleton_MissingDependency_Throws()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<ServiceWithDependency>();

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => collection.BuildServiceProvider());

        Assert.That(exception!.Message, Does.Contain(nameof(SimpleService)));
    }

    [Test]
    public void AddSingleton_CircularDependency_Throws()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<CircularServiceA>();
        collection.AddSingleton<CircularServiceB>();

        Assert.Throws<InvalidOperationException>(() => collection.BuildServiceProvider());
    }

    // --- AddSingleton<T>(T instance) ---

    [Test]
    public void AddSingleton_Instance_ReturnsExactInstance()
    {
        ServiceCollection collection = new();
        SimpleService instance = new();
        collection.AddSingleton(instance);

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<SimpleService>(), Is.SameAs(instance));
    }

    [Test]
    public void AddSingleton_Instance_Duplicate_LastWins()
    {
        ServiceCollection collection = new();
        SimpleService first = new();
        SimpleService second = new();
        collection.AddSingleton(first);
        collection.AddSingleton(second);

        ServiceProvider provider = collection.BuildServiceProvider();

        // Second registration wins
        Assert.That(provider.GetRequiredService<SimpleService>(), Is.SameAs(second));
    }

    // --- AddSingleton<T>(Delegate) ---

    [Test]
    public void AddSingleton_Factory_ResolvesFromFactory()
    {
        ServiceCollection collection = new();
        SimpleService expected = new();
        collection.AddSingleton<SimpleService>(() => expected);

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<SimpleService>(), Is.SameAs(expected));
    }

    [Test]
    public void AddSingleton_Factory_WithDependencyParameter_ResolvesDependency()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<ServiceWithDependency>((SimpleService s) => new ServiceWithDependency(s));

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithDependency service = provider.GetRequiredService<ServiceWithDependency>();

        Assert.That(service.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void AddSingleton_Factory_Duplicate_LastWins()
    {
        ServiceCollection collection = new();
        SimpleService first = new();
        SimpleService second = new();
        collection.AddSingleton<SimpleService>(() => first);
        collection.AddSingleton<SimpleService>(() => second);

        ServiceProvider provider = collection.BuildServiceProvider();

        // Second factory registration wins
        Assert.That(provider.GetRequiredService<SimpleService>(), Is.SameAs(second));
    }

    // --- AddSingleton<TService, TFactory>() instance factory ---

    [Test]
    public void AddSingleton_InstanceFactory_ResolvesFromFactoryMethod()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<TestFactory>();
        collection.AddSingleton<FactoryProduct, TestFactory>();

        ServiceProvider provider = collection.BuildServiceProvider();

        FactoryProduct product = provider.GetRequiredService<FactoryProduct>();
        Assert.That(product.Value, Is.EqualTo("from-factory"));
    }

    [Test]
    public void AddSingleton_InstanceFactory_WithDependency_ResolvesDependency()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<TestFactory>();
        collection.AddSingleton<FactoryProductWithDependency, TestFactory>();

        ServiceProvider provider = collection.BuildServiceProvider();

        FactoryProductWithDependency product = provider.GetRequiredService<FactoryProductWithDependency>();
        Assert.That(product.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void AddSingleton_InstanceFactory_ReturnsSameInstance()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<TestFactory>();
        collection.AddSingleton<FactoryProduct, TestFactory>();

        ServiceProvider provider = collection.BuildServiceProvider();

        FactoryProduct first = provider.GetRequiredService<FactoryProduct>();
        FactoryProduct second = provider.GetRequiredService<FactoryProduct>();
        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void AddSingleton_InstanceFactory_InheritedMethod_ResolvesFromBaseFactory()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<DerivedFactory>();
        collection.AddSingleton<BaseProduct, DerivedFactory>();

        ServiceProvider provider = collection.BuildServiceProvider();

        BaseProduct product = provider.GetRequiredService<BaseProduct>();
        Assert.That(product.Source, Is.EqualTo("from-base"));
    }

    // --- AddSingleton<TService, TImplementation>() ---

    [Test]
    public void AddSingleton_WithAlias_ResolvesViaInterface()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IMyService service = provider.GetRequiredService<IMyService>();

        Assert.That(service, Is.InstanceOf<MyServiceImpl>());
    }

    [Test]
    public void AddSingleton_WithAlias_OnlyRegistersUnderServiceType_NotImplType()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        // TImpl is NOT separately registered — only TService is
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<MyServiceImpl>());
    }

    [Test]
    public void AddSingleton_WithAlias_InternalConstructor_ResolvesViaInterface()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<IMyService, InternalConstructorMyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IMyService service = provider.GetRequiredService<IMyService>();

        Assert.That(service, Is.InstanceOf<InternalConstructorMyServiceImpl>());
        Assert.That(((InternalConstructorMyServiceImpl)service).Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void AddSingleton_WithAlias_DuplicateServiceType_LastWins()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        // Second registration wins
        Assert.That(provider.GetRequiredService<IMyService>(), Is.InstanceOf<AnotherServiceImpl>());
    }

    // --- AddAlias ---

    [Test]
    public void AddAlias_ResolvesViaInterface()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<MyServiceImpl>();
        collection.AddAlias<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IMyService service = provider.GetRequiredService<IMyService>();

        Assert.That(service, Is.InstanceOf<MyServiceImpl>());
        Assert.That(service, Is.SameAs(provider.GetRequiredService<MyServiceImpl>()));
    }

    [Test]
    public void AddAlias_Duplicate_LastWins()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<MyServiceImpl>();
        collection.AddSingleton<AnotherServiceImpl>();
        collection.AddAlias<IMyService, MyServiceImpl>();

        // Second alias for the same TService should not throw — last wins
        Assert.DoesNotThrow(() => collection.AddAlias<IMyService, AnotherServiceImpl>());
    }

    // --- GetServices ---

    [Test]
    public void GetServices_ReturnsEmptyCollection_WhenNothingRegistered()
    {
        ServiceCollection collection = new();
        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();

        Assert.That(services, Is.Empty);
    }

    [Test]
    public void GetServices_ReturnsSingleRegistration()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();

        Assert.That(services, Has.Count.EqualTo(1));
        Assert.That(services[0], Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void GetServices_ReturnsAllRegistrationsInOrder()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<IMyService> services = provider.GetServices<IMyService>();

        Assert.That(services, Has.Count.EqualTo(2));
        Assert.That(services[0], Is.InstanceOf<MyServiceImpl>());
        Assert.That(services[1], Is.InstanceOf<AnotherServiceImpl>());
    }

    [Test]
    public void GetServices_LastRegistrationMatchesGetService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<IMyService> services = provider.GetServices<IMyService>();

        // GetRequiredService<T> returns last-wins; GetServices returns all
        Assert.That(provider.GetRequiredService<IMyService>(), Is.SameAs(services[1]));
    }

    [Test]
    public void GetServices_AddSingletonDuplicate_CollectsAll()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();

        Assert.That(services, Has.Count.EqualTo(2));
    }

    // GetServices<T>() returns a cached T[] directly (see ServiceProvider._serviceCollections).
    // These two tests lock in the zero-alloc invariant — regression would silently re-allocate.

    [Test]
    public void GetServices_ReturnsSameInstanceAcrossCalls()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<IMyService> first = provider.GetServices<IMyService>();
        IReadOnlyList<IMyService> second = provider.GetServices<IMyService>();

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void GetServices_ReturnsRuntimeTypedArray()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IReadOnlyList<IMyService> services = provider.GetServices<IMyService>();

        // Runtime type must be IMyService[] so Unsafe.As<T[]> is sound, not object[]
        Assert.That(services.GetType(), Is.EqualTo(typeof(IMyService[])));
    }

    [Test]
    public void GetServices_FactoryCalledOncePerRegistration()
    {
        ServiceCollection collection = new();
        List<object> activated = new();
        collection.OnActivated((obj, _) => activated.Add(obj));

        // Register the same service type twice; both instances must go through OnActivated
        collection.AddSingleton<SimpleService>(() => new SimpleService());
        collection.AddSingleton<SimpleService>(() => new SimpleService());

        ServiceProvider provider = collection.BuildServiceProvider();
        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();

        Assert.That(services, Has.Count.EqualTo(2));
        // Every instance returned by GetServices must have triggered OnActivated
        Assert.That(activated, Has.Count.EqualTo(2));
        Assert.That(activated, Contains.Item(services[0]));
        Assert.That(activated, Contains.Item(services[1]));
    }

    // --- GetRequiredService / GetService ---

    [Test]
    public void GetRequiredService_Unregistered_Throws()
    {
        ServiceCollection collection = new();
        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SimpleService>());
    }

    [Test]
    public void GetService_Unregistered_ReturnsNull()
    {
        ServiceCollection collection = new();
        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetService<SimpleService>(), Is.Null);
    }

    [Test]
    public void GetService_Registered_ReturnsService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetService<SimpleService>(), Is.Not.Null);
    }

    // --- ServiceProvider self-registration ---

    [Test]
    public void ServiceProvider_IsResolvable()
    {
        ServiceCollection collection = new();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<ServiceProvider>(), Is.SameAs(provider));
    }

    [Test]
    public void ServiceProvider_InjectableViaConstructor()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<ServiceNeedingProvider>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceNeedingProvider service = provider.GetRequiredService<ServiceNeedingProvider>();

        Assert.That(service.Provider, Is.SameAs(provider));
    }

    // --- IsRegistered ---

    [Test]
    public void IsRegistered_ReturnsTrueForRegisteredType()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        Assert.That(collection.IsRegistered<SimpleService>(), Is.True);
    }

    [Test]
    public void IsRegistered_ReturnsFalseForUnregisteredType()
    {
        ServiceCollection collection = new();

        Assert.That(collection.IsRegistered<SimpleService>(), Is.False);
    }

    // --- OnActivated ---

    [Test]
    public void OnActivated_CalledForEachInstance()
    {
        ServiceCollection collection = new();
        List<object> activated = new();
        collection.OnActivated((obj, _) => activated.Add(obj));

        collection.AddSingleton<SimpleService>();
        collection.AddSingleton<AnotherService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(activated, Has.Count.EqualTo(2));
        Assert.That(activated, Has.Some.InstanceOf<SimpleService>());
        Assert.That(activated, Has.Some.InstanceOf<AnotherService>());
    }

    [Test]
    public void OnActivated_CalledForInstanceRegistration()
    {
        ServiceCollection collection = new();
        List<object> activated = new();
        collection.OnActivated((obj, _) => activated.Add(obj));

        SimpleService instance = new();
        collection.AddSingleton(instance);

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(activated, Has.Count.EqualTo(1));
        Assert.That(activated[0], Is.SameAs(instance));
    }

    [Test]
    public void OnActivated_CalledForFactoryRegistration()
    {
        ServiceCollection collection = new();
        List<object> activated = new();
        collection.OnActivated((obj, _) => activated.Add(obj));

        collection.AddSingleton<SimpleService>(() => new SimpleService());

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(activated, Has.Count.EqualTo(1));
        Assert.That(activated[0], Is.InstanceOf<SimpleService>());
    }

    [Test]
    public void OnActivated_NotCalledForAlias()
    {
        ServiceCollection collection = new();
        List<object> activated = new();
        collection.OnActivated((obj, _) => activated.Add(obj));

        collection.AddSingleton<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        // Only one activation: the concrete type, not the alias
        Assert.That(activated, Has.Count.EqualTo(1));
        Assert.That(activated[0], Is.InstanceOf<MyServiceImpl>());
    }

    // --- ServiceRegistry ---

    [Test]
    public void AddRegistry_TracksActivatedMatchingSingletons()
    {
        ServiceCollection collection = new();
        collection.AddRegistry<IMyService>();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<AnotherService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceRegistry<IMyService> registry = provider.GetRequiredService<ServiceRegistry<IMyService>>();
        IMyService[] services = registry.ToArray();

        Assert.That(services, Has.Length.EqualTo(1));
        Assert.That(services[0], Is.InstanceOf<MyServiceImpl>());
    }

    [Test]
    public void ServiceRegistry_VersionChangesWhenServiceIsAddedAndRemoved()
    {
        ServiceCollection rootServices = new();
        rootServices.AddRegistry<IMyService>();
        using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = rootProvider.GetRequiredService<ServiceRegistry<IMyService>>();
        Assert.That(registry.Version, Is.EqualTo(0UL));

        ServiceCollection childServices = rootProvider.CreateServiceCollection();
        childServices.AddSingleton<IMyService>(new MyServiceImpl());
        ServiceProvider childProvider = childServices.BuildServiceProvider();

        Assert.That(registry.Version, Is.EqualTo(0UL));
        Assert.That(registry.ToArray(), Has.Length.EqualTo(1));
        Assert.That(registry.Version, Is.EqualTo(1UL));

        childProvider.Dispose();

        Assert.That(registry.Version, Is.EqualTo(2UL));
    }

    [Test]
    public void ServiceRegistry_DuplicateActivationDoesNotChangeVersion()
    {
        MyServiceImpl service = new();
        ServiceCollection collection = new();
        collection.AddRegistry<IMyService>();
        collection.AddSingleton<IMyService>(service);
        collection.AddSingleton<IMyService>(service);
        using ServiceProvider provider = collection.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = provider.GetRequiredService<ServiceRegistry<IMyService>>();

        Assert.That(registry.ToArray(), Has.Length.EqualTo(1));
        Assert.That(registry.Version, Is.EqualTo(1UL));
    }

    [Test]
    public void ServiceRegistry_PendingServiceRemovedBeforePublicationDoesNotChangeVersion()
    {
        ServiceCollection rootServices = new();
        rootServices.AddRegistry<IMyService>();
        using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = rootProvider.GetRequiredService<ServiceRegistry<IMyService>>();
        ServiceCollection childServices = rootProvider.CreateServiceCollection();
        childServices.AddSingleton<IMyService>(new MyServiceImpl());
        ServiceProvider childProvider = childServices.BuildServiceProvider();

        childProvider.Dispose();

        Assert.That(registry.ToArray(), Is.Empty);
        Assert.That(registry.Version, Is.EqualTo(0UL));
    }

    [Test]
    public void ServiceRegistry_GetEnumerator_ReturnsValueTypeEnumerator()
    {
        ServiceCollection collection = new();
        collection.AddRegistry<IMyService>();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        ServiceProvider provider = collection.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = provider.GetRequiredService<ServiceRegistry<IMyService>>();

        ServiceRegistry<IMyService>.Enumerator enumerator = registry.GetEnumerator();

        try
        {
            Assert.That(typeof(ServiceRegistry<IMyService>.Enumerator).IsValueType, Is.True);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.InstanceOf<MyServiceImpl>());
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    [Test]
    public void AddRegistry_DefersChildServicesAddedDuringEnumeration()
    {
        MyServiceImpl first = new();
        AnotherServiceImpl second = new();
        ServiceCollection rootServices = new();
        rootServices.AddRegistry<IMyService>();
        rootServices.AddSingleton<IMyService>(first);
        using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = rootProvider.GetRequiredService<ServiceRegistry<IMyService>>();
        ServiceRegistry<IMyService>.Enumerator enumerator = registry.GetEnumerator();
        Assert.That(registry.Version, Is.EqualTo(1UL));

        try
        {
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.SameAs(first));

            ServiceCollection childServices = rootProvider.CreateServiceCollection();
            childServices.AddSingleton<IMyService>(second);
            _ = childServices.BuildServiceProvider();

            Assert.That(registry.Version, Is.EqualTo(1UL));
            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(registry.ToArray(), Is.EqualTo(new[] { first }));
            Assert.That(registry.Version, Is.EqualTo(1UL));
        }
        finally
        {
            enumerator.Dispose();
        }

        Assert.That(registry.ToArray(), Is.EqualTo(new IMyService[] { first, second }));
        Assert.That(registry.Version, Is.EqualTo(2UL));
    }

    [Test]
    public void AddRegistry_SkipsChildServiceRemovedDuringEnumeration()
    {
        MyServiceImpl first = new();
        AnotherServiceImpl second = new();
        ServiceCollection rootServices = new();
        rootServices.AddRegistry<IMyService>();
        using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
        ServiceCollection firstServices = rootProvider.CreateServiceCollection();
        firstServices.AddSingleton<IMyService>(first);
        using ServiceProvider firstProvider = firstServices.BuildServiceProvider();
        ServiceCollection secondServices = rootProvider.CreateServiceCollection();
        secondServices.AddSingleton<IMyService>(second);
        ServiceProvider secondProvider = secondServices.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = rootProvider.GetRequiredService<ServiceRegistry<IMyService>>();
        ServiceRegistry<IMyService>.Enumerator enumerator = registry.GetEnumerator();

        try
        {
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.SameAs(first));

            secondProvider.Dispose();

            Assert.That(enumerator.MoveNext(), Is.False);
            Assert.That(registry.ToArray(), Is.EqualTo(new[] { first }));
        }
        finally
        {
            enumerator.Dispose();
        }

        Assert.That(registry.ToArray(), Is.EqualTo(new[] { first }));
    }

    [Test]
    public void AddRegistry_AppliesPendingAdditionsBeforeNextOutermostEnumeration()
    {
        MyServiceImpl first = new();
        AnotherServiceImpl second = new();
        ServiceCollection rootServices = new();
        rootServices.AddRegistry<IMyService>();
        rootServices.AddSingleton<IMyService>(first);
        using ServiceProvider rootProvider = rootServices.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = rootProvider.GetRequiredService<ServiceRegistry<IMyService>>();
        ServiceRegistry<IMyService>.Enumerator outer = registry.GetEnumerator();
        ServiceRegistry<IMyService>.Enumerator inner = registry.GetEnumerator();

        try
        {
            ServiceCollection childServices = rootProvider.CreateServiceCollection();
            childServices.AddSingleton<IMyService>(second);
            _ = childServices.BuildServiceProvider();

            inner.Dispose();
            Assert.That(registry.ToArray(), Is.EqualTo(new[] { first }));
        }
        finally
        {
            inner.Dispose();
            outer.Dispose();
        }

        Assert.That(registry.ToArray(), Is.EqualTo(new IMyService[] { first, second }));
    }

    [Test]
    public void AddRegistry_DoesNotReapplyComparisonWithoutPendingAdditions()
    {
        MyServiceImpl first = new();
        AnotherServiceImpl second = new();
        bool reverse = false;
        ServiceCollection collection = new();
        collection.AddRegistry<IMyService>((left, right) =>
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            bool leftIsFirst = ReferenceEquals(left, first);
            return leftIsFirst == reverse ? 1 : -1;
        });
        collection.AddSingleton<IMyService>(first);
        collection.AddSingleton<IMyService>(second);
        using ServiceProvider provider = collection.BuildServiceProvider();
        ServiceRegistry<IMyService> registry = provider.GetRequiredService<ServiceRegistry<IMyService>>();

        Assert.That(registry.ToArray(), Is.EqualTo(new IMyService[] { first, second }));

        reverse = true;

        Assert.That(registry.ToArray(), Is.EqualTo(new IMyService[] { first, second }));
    }

    [Test]
    public void AddRegistry_DoesNotActivateTransientsDuringBuild()
    {
        ServiceCollection collection = new();
        collection.AddRegistry<IMyService>();
        collection.AddTransient<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceRegistry<IMyService> registry = provider.GetRequiredService<ServiceRegistry<IMyService>>();
        Assert.That(registry.ToArray(), Is.Empty);

        IMyService service = provider.GetRequiredService<IMyService>();

        Assert.That(registry.ToArray(), Is.EqualTo(new[] { service }));
    }

    [Test]
    public void AddRegistry_UnsubscribesServicesOnProviderDispose()
    {
        ServiceCollection collection = new();
        collection.AddRegistry<IDisposableFoo>();
        collection.AddSingleton<IDisposableFoo, DisposableFooImpl>();
        ServiceProvider provider = collection.BuildServiceProvider();
        ServiceRegistry<IDisposableFoo> registry = provider.GetRequiredService<ServiceRegistry<IDisposableFoo>>();

        Assert.That(registry.ToArray(), Has.Length.EqualTo(1));

        provider.Dispose();

        Assert.That(registry.ToArray(), Is.Empty);
    }

    // --- OnStart ---

    [Test]
    public void OnStart_CalledAfterAllResolved()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        SimpleService? captured = null;
        collection.OnStart((SimpleService s) => { captured = s; });

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(captured, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void OnStart_CalledInOrder()
    {
        ServiceCollection collection = new();
        List<int> order = new();

        collection.OnStart(() => { order.Add(1); });
        collection.OnStart(() => { order.Add(2); });

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(order, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void GetServices_DuringOnStart_ReturnsAllMultiRegistrations()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();

        OnStartCapturer capturer = new();
        collection.AddSingleton(capturer);
        collection.OnStart((OnStartCapturer c, ServiceProvider sp) => c.Capture(sp));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(capturer.CapturedServices<IMyService>(), Is.Not.Null);
        Assert.That(capturer.CapturedServices<IMyService>()!, Has.Count.EqualTo(2));
        Assert.That(capturer.CapturedServices<IMyService>()![0], Is.InstanceOf<MyServiceImpl>());
        Assert.That(capturer.CapturedServices<IMyService>()![1], Is.InstanceOf<AnotherServiceImpl>());

        IReadOnlyList<IMyService> afterBuild = provider.GetServices<IMyService>();
        Assert.That(afterBuild, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetRequiredService_DuringOnStart_ReturnsSameInstanceAsAfterBuild()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        OnStartCapturer capturer = new();
        collection.AddSingleton(capturer);
        collection.OnStart((OnStartCapturer c, ServiceProvider sp) => c.Capture(sp));

        ServiceProvider provider = collection.BuildServiceProvider();

        SimpleService resolvedDuringOnStart = capturer.CapturedProvider!.GetRequiredService<SimpleService>();

        Assert.That(resolvedDuringOnStart, Is.SameAs(provider.GetRequiredService<SimpleService>()));

        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();
        Assert.That(services, Has.Count.EqualTo(1));
        Assert.That(services[0], Is.SameAs(resolvedDuringOnStart));
    }

    [Test]
    public void GetServices_AfterOnStart_ReturnsExactlyPreRegisteredSet()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();

        bool onStartFired = false;
        collection.OnStart(() => { onStartFired = true; });

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(onStartFired, Is.True);

        IReadOnlyList<IMyService> afterBuild = provider.GetServices<IMyService>();
        Assert.That(afterBuild, Has.Count.EqualTo(1));
        Assert.That(afterBuild[0], Is.InstanceOf<MyServiceImpl>());
    }

    [Test]
    public void Dispose_DisposesDisposableServices()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<DisposableService>();

        ServiceProvider provider = collection.BuildServiceProvider();
        DisposableService service = provider.GetRequiredService<DisposableService>();

        Assert.That(service.Disposed, Is.False);

        provider.Dispose();

        Assert.That(service.Disposed, Is.True);
    }

    // --- Subcontainers ---

    [Test]
    public void ChildContainer_ResolvesOwnServices()
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddSingleton<SimpleService>();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        childCollection.AddSingleton<AnotherService>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetRequiredService<AnotherService>(), Is.Not.Null);
    }

    [Test]
    public void ChildContainer_FallsBackToParent()
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddSingleton<SimpleService>();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        childCollection.AddSingleton<AnotherService>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetRequiredService<SimpleService>(), Is.SameAs(root.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void ChildContainer_ConstructorResolvesFromParent()
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddSingleton<SimpleService>();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        childCollection.AddSingleton<ServiceWithDependency>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        ServiceWithDependency service = child.GetRequiredService<ServiceWithDependency>();

        Assert.That(service.Simple, Is.SameAs(root.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void ChildContainer_OwnServiceProviderIsSelf()
    {
        ServiceCollection rootCollection = new();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetRequiredService<ServiceProvider>(), Is.SameAs(child));
    }

    [Test]
    public void ChildContainer_CanOverrideParentService()
    {
        ServiceCollection rootCollection = new();
        SimpleService rootInstance = new();
        rootCollection.AddSingleton(rootInstance);
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        SimpleService childInstance = new();
        childCollection.AddSingleton(childInstance);
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetRequiredService<SimpleService>(), Is.SameAs(childInstance));
        Assert.That(root.GetRequiredService<SimpleService>(), Is.SameAs(rootInstance));
    }

    [Test]
    public void ChildContainer_GetService_FallsBackToParent()
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddSingleton<SimpleService>();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetService<SimpleService>(), Is.SameAs(root.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void ChildContainer_GetService_ReturnsNullIfNowhere()
    {
        ServiceCollection rootCollection = new();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetService<SimpleService>(), Is.Null);
    }

    // --- Registration order independence ---

    [Test]
    public void AddSingleton_DependencyRegisteredAfter_StillResolves()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<ServiceWithDependency>();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithDependency service = provider.GetRequiredService<ServiceWithDependency>();

        Assert.That(service.Simple, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }
    // --- Double dispose ---

    [Test]
    public void Dispose_CalledTwice_OnlyFiresCallbacksOnce()
    {
        ServiceCollection collection = new();
        int disposingCount = 0;
        collection.AddSingleton<SimpleService>();
        collection.OnDisposing((_, _) => disposingCount++);

        ServiceProvider provider = collection.BuildServiceProvider();
        provider.Dispose();
        provider.Dispose();

        Assert.That(disposingCount, Is.EqualTo(1));
    }

    // --- Parent-child alias ---

    [Test]
    public void ChildContainer_AliasToParentType_Resolves()
    {
        ServiceCollection rootCollection = new();
        rootCollection.AddSingleton<MyServiceImpl>();
        ServiceProvider root = rootCollection.BuildServiceProvider();

        ServiceCollection childCollection = root.CreateServiceCollection();
        childCollection.AddSingleton<IMyService, MyServiceImpl>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetRequiredService<IMyService>(), Is.InstanceOf<MyServiceImpl>());
    }

    // --- GetService during build ---

    [Test]
    public void GetService_DuringBuild_ResolvesRegisteredService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<SimpleService>();

        SimpleService? captured = null;
        collection.AddSingleton<AnotherService>((ServiceProvider sp) =>
        {
            captured = sp.GetService<SimpleService>();
            return new AnotherService();
        });

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(captured, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void GetService_DuringBuild_ReturnsNullForUnregistered()
    {
        ServiceCollection collection = new();

        SimpleService? captured = null;
        collection.AddSingleton<AnotherService>((ServiceProvider sp) =>
        {
            captured = sp.GetService<SimpleService>();
            return new AnotherService();
        });

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(captured, Is.Null);
    }

    // --- Factory returning null ---

    [Test]
    public void AddSingleton_Factory_ReturnsNull_ContributesNoService()
    {
        ServiceCollection collection = new();
        int factoryCalls = 0;
        collection.AddSingleton<SimpleService>((Func<SimpleService?>)(() =>
        {
            factoryCalls++;
            return null;
        }));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(factoryCalls, Is.EqualTo(1));
        Assert.That(collection.IsRegistered<SimpleService>(), Is.True);
        Assert.That(provider.GetService<SimpleService>(), Is.Null);
        Assert.That(provider.GetServices<SimpleService>(), Is.Empty);
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SimpleService>());
    }

    [Test]
    public void AddSingleton_DelegateFactory_UsesDependencyToConditionallyContributeService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IConditionalService>((FactoryCondition condition) =>
            condition.Enabled ? new ConditionalService() : null);
        collection.AddSingleton(new FactoryCondition(false));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetService<IConditionalService>(), Is.Null);
    }

    // --- ServiceCollection reuse ---

    [Test]
    public void BuildServiceProvider_Twice_ProducesIndependentProviders()
    {
        ServiceCollection collection = new();
        int disposingCount = 0;
        collection.OnDisposing((_, _) => disposingCount++);
        collection.AddSingleton<SimpleService>();

        ServiceProvider first = collection.BuildServiceProvider();
        ServiceProvider second = collection.BuildServiceProvider();
        SimpleService firstService = first.GetRequiredService<SimpleService>();

        first.Dispose();

        Assert.That(disposingCount, Is.EqualTo(1));
        Assert.That(second.GetRequiredService<SimpleService>(), Is.Not.SameAs(firstService));
    }

    // --- Dispose order must be reverse creation order ---

    [Test]
    public void Dispose_DisposesServicesInReverseCreationOrder()
    {
        List<string> disposeLog = new();

        ServiceCollection collection = new();
        // Register A, then B, then C — creation order is A, B, C
        collection.AddSingleton<TrackingDisposableA>(new TrackingDisposableA(disposeLog));
        collection.AddSingleton<TrackingDisposableB>(new TrackingDisposableB(disposeLog));
        collection.AddSingleton<TrackingDisposableC>(new TrackingDisposableC(disposeLog));

        ServiceProvider provider = collection.BuildServiceProvider();
        provider.Dispose();

        // Expected: reverse creation order C, B, A
        Assert.That(disposeLog, Is.EqualTo(new[] { "C", "B", "A" }));
    }

    // --- GetServices called during build must not corrupt the last-wins factory slot ---

    [Test]
    public void GetServices_CalledDuringBuild_LastWinsFactoryResolvesToLastInstance()
    {
        ServiceCollection collection = new();
        MyServiceImpl firstInstance = new();
        AnotherServiceImpl lastInstance = new();

        // Registered first so it is resolved before IMyService in the main build loop,
        // ensuring GetServices<IMyService> fires while the last-wins slot is still empty.
        collection.AddSingleton<AnotherService>((ServiceProvider sp) =>
        {
            sp.GetServices<IMyService>();
            return new AnotherService();
        });

        collection.AddSingleton<IMyService>((ServiceProvider _) => firstInstance);
        collection.AddSingleton<IMyService>((ServiceProvider _) => lastInstance);

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IMyService>(), Is.SameAs(lastInstance));

        IReadOnlyList<IMyService> services = provider.GetServices<IMyService>();
        Assert.That(services, Has.Count.EqualTo(2));
        Assert.That(services[0], Is.SameAs(firstInstance));
        Assert.That(services[1], Is.SameAs(lastInstance));
    }

    // --- GetServices called during build must resolve alias sources eagerly ---

    [Test]
    public void GetServices_CalledDuringBuild_IncludesAliasSource()
    {
        ServiceCollection collection = new();
        IReadOnlyList<IMyService>? capturedDuringBuild = null;

        // Registered first so it is resolved before MyServiceImpl in the main build loop,
        // ensuring GetServices<IMyService> fires while the alias source is still unresolved.
        collection.AddSingleton<AnotherService>((ServiceProvider sp) =>
        {
            capturedDuringBuild = sp.GetServices<IMyService>();
            return new AnotherService();
        });

        collection.AddSingleton<MyServiceImpl>();
        collection.AddAlias<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(capturedDuringBuild, Has.Count.EqualTo(1));
        Assert.That(capturedDuringBuild![0], Is.InstanceOf<MyServiceImpl>());
    }

    // --- AddAlias guard ---

    [Test]
    public void AddAlias_WithoutPriorRegistration_Throws()
    {
        ServiceCollection collection = new();

        Assert.Throws<InvalidOperationException>(() => collection.AddAlias<IMyService, MyServiceImpl>());
    }

    // --- GetServices child→parent fallback ---

    [Test]
    public void GetServices_ChildFallsBackToParent_WhenTypeNotInChild()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddSingleton<IMyService, MyServiceImpl>();
        parentCollection.AddSingleton<IMyService, AnotherServiceImpl>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<SimpleService>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        IReadOnlyList<IMyService> services = child.GetServices<IMyService>();

        Assert.That(services, Has.Count.EqualTo(2));
        Assert.That(services[0], Is.InstanceOf<MyServiceImpl>());
        Assert.That(services[1], Is.InstanceOf<AnotherServiceImpl>());
    }

    // --- OnActivated for non-last-wins Instance ---

    [Test]
    public void OnActivated_CalledForAllInstances_IncludingNonLastWins()
    {
        ServiceCollection collection = new();
        List<object> activated = new();
        collection.OnActivated((obj, _) => activated.Add(obj));

        SimpleService first = new();
        SimpleService second = new();
        collection.AddSingleton(first);
        collection.AddSingleton(second);

        ServiceProvider provider = collection.BuildServiceProvider();
        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();

        Assert.That(services, Has.Count.EqualTo(2));
        Assert.That(activated, Has.Count.EqualTo(2));
        Assert.That(activated, Contains.Item(first));
        Assert.That(activated, Contains.Item(second));
    }

    // --- OnDisposing per-build snapshot isolation ---

    [Test]
    public void OnDisposing_SnapshotAtBuildTime_SecondProviderHasBothCallbacks()
    {
        ServiceCollection collection = new();
        int firstCallbackCount = 0;
        int secondCallbackCount = 0;
        collection.AddSingleton<SimpleService>();
        collection.OnDisposing((_, _) => firstCallbackCount++);

        // Build first before adding the second callback
        ServiceProvider first = collection.BuildServiceProvider();

        collection.OnDisposing((_, _) => secondCallbackCount++);

        // Build second after adding the second callback
        ServiceProvider second = collection.BuildServiceProvider();

        first.Dispose();

        // First provider was built with only one callback
        Assert.That(firstCallbackCount, Is.EqualTo(1));
        Assert.That(secondCallbackCount, Is.EqualTo(0));

        second.Dispose();

        // Second provider was built with both callbacks — firstCallbackCount gets another increment
        Assert.That(firstCallbackCount, Is.EqualTo(2));
        Assert.That(secondCallbackCount, Is.EqualTo(1));
    }

    // --- Dispose ordering: OnDisposing fires before IDisposable.Dispose ---

    [Test]
    public void Dispose_OnDisposingCallbackFiresBeforeServiceDispose()
    {
        List<string> order = new();
        DisposableOrderTracker tracker = new(order, "service");

        ServiceCollection collection = new();
        collection.AddSingleton(tracker);
        collection.OnDisposing((instance, _) =>
        {
            if (instance is DisposableOrderTracker)
            {
                order.Add("OnDisposing");
            }
        });

        ServiceProvider provider = collection.BuildServiceProvider();
        // Ensure the service is resolved so it participates in disposal
        provider.GetRequiredService<DisposableOrderTracker>();
        provider.Dispose();

        Assert.That(order[0], Is.EqualTo("OnDisposing"));
        Assert.That(order[1], Is.EqualTo("service"));
    }

    // --- Dispose of service registered via AddSingleton<IFoo, DisposableFoo>() ---

    [Test]
    public void Dispose_ImplementationRegisteredViaAlias_IsDisposed()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IDisposableFoo, DisposableFooImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();
        IDisposableFoo service = provider.GetRequiredService<IDisposableFoo>();

        provider.Dispose();

        Assert.That(((DisposableFooImpl)service).Disposed, Is.True);
    }

    // --- Circular detection in ResolveNonLastDescriptor ---

    [Test]
    public void GetServices_NonLastWinsFactory_CircularViaLastWins_ThrowsOrReturnsStable()
    {
        ServiceCollection collection = new();

        // Register a non-last-wins factory for IMyService that requests CircularServiceA,
        // and CircularServiceA depends on CircularServiceB which depends on CircularServiceA.
        // The circular dependency is in the type-constructor graph, detected at build time.
        collection.AddSingleton<CircularServiceA>();
        collection.AddSingleton<CircularServiceB>();
        collection.AddSingleton<IMyService>((ServiceProvider sp) =>
        {
            sp.GetRequiredService<CircularServiceA>();
            return new MyServiceImpl();
        });
        collection.AddSingleton<IMyService>((ServiceProvider _) => new AnotherServiceImpl());

        // Circular in constructor graph is detected during BuildServiceProvider
        Assert.Throws<InvalidOperationException>(() => collection.BuildServiceProvider());
    }

    // --- Null factory in ResolveNonLastDescriptor ---

    [Test]
    public void GetServices_NonLastWinsFactory_ReturnsNull_ExcludesResult()
    {
        ServiceCollection collection = new();

        collection.AddSingleton<SimpleService>((Func<ServiceProvider, SimpleService?>)(_ => null));
        collection.AddSingleton<SimpleService>((Func<ServiceProvider, SimpleService>)(_ => new SimpleService()));

        ServiceProvider provider = collection.BuildServiceProvider();
        IReadOnlyList<SimpleService> services = provider.GetServices<SimpleService>();

        Assert.That(services, Has.Count.EqualTo(1));
        Assert.That(provider.GetRequiredService<SimpleService>(), Is.SameAs(services[0]));
    }

    [Test]
    public void GetRequiredService_LastNullSingletonFactory_FallsBackToEarlierRegistration()
    {
        SimpleService fallback = new();
        ServiceCollection collection = new();
        collection.AddSingleton(fallback);
        collection.AddSingleton<SimpleService>((Func<ServiceProvider, SimpleService?>)(_ => null));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<SimpleService>(), Is.SameAs(fallback));
        Assert.That(provider.GetServices<SimpleService>(), Is.EqualTo(new[] { fallback }));
    }

    [Test]
    public void AddAlias_ToAbsentSingletonImplementation_ContributesNoService()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<MyServiceImpl>(
            (Func<ServiceProvider, MyServiceImpl?>)(_ => null));
        collection.AddAlias<IMyService, MyServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetService<IMyService>(), Is.Null);
        Assert.That(provider.GetServices<IMyService>(), Is.Empty);
    }

    // --- Aliased services must not be double-disposed ---

    [Test]
    public void Dispose_AliasedService_DisposedExactlyOnce()
    {
        CountingDisposable instance = new();

        ServiceCollection collection = new();
        collection.AddSingleton<CountingDisposable>(instance);
        collection.AddAlias<IDisposableAlias, CountingDisposable>();

        ServiceProvider provider = collection.BuildServiceProvider();
        provider.Dispose();

        // The same instance sits in two slots; Dispose() must be called exactly once
        Assert.That(instance.DisposeCallCount, Is.EqualTo(1));
    }

    // --- IEnumerable<T> resolution (TDD red: not implemented yet) ---

    [Test]
    public void GetRequiredService_IEnumerable_ReturnsAllRegistrations()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();

        ServiceProvider provider = collection.BuildServiceProvider();

        IEnumerable<IMyService> services = provider.GetRequiredService<IEnumerable<IMyService>>();

        Assert.That(services, Is.Not.Null);
        Assert.That(services.Count(), Is.EqualTo(2));
    }

    [Test]
    public void GetService_IEnumerable_NeverReturnsNull()
    {
        ServiceCollection collection = new();
        ServiceProvider provider = collection.BuildServiceProvider();

        IEnumerable<IMyService>? services = provider.GetService<IEnumerable<IMyService>>();

        // IEnumerable<T> resolution never returns null — empty if nothing registered
        Assert.That(services, Is.Not.Null);
        Assert.That(services!.Count(), Is.EqualTo(0));
    }

    [Test]
    public void GetService_IEnumerable_ReturnsEmptyWhenNothingRegistered()
    {
        ServiceCollection collection = new();
        ServiceProvider provider = collection.BuildServiceProvider();

        IEnumerable<IMyService>? services = provider.GetService<IEnumerable<IMyService>>();

        Assert.That(services, Is.Not.Null);
        Assert.That(services!.Any(), Is.False);
    }

    [Test]
    public void ChildContainer_GetRequiredService_IEnumerable_FallsBackToParent()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddSingleton<IMyService, MyServiceImpl>();
        parentCollection.AddSingleton<IMyService, AnotherServiceImpl>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        ServiceProvider child = childCollection.BuildServiceProvider();

        IEnumerable<IMyService> services = child.GetRequiredService<IEnumerable<IMyService>>();

        Assert.That(services.Count(), Is.EqualTo(2));
    }

    [Test]
    public void ConstructorInjection_IEnumerable_ReceivesAllRegistrations()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<IMyService, MyServiceImpl>();
        collection.AddSingleton<IMyService, AnotherServiceImpl>();
        collection.AddSingleton<ServiceWithEnumerableDependency>();

        ServiceProvider provider = collection.BuildServiceProvider();

        ServiceWithEnumerableDependency service = provider.GetRequiredService<ServiceWithEnumerableDependency>();

        Assert.That(service.Services, Is.Not.Null);
        Assert.That(service.Services.Count(), Is.EqualTo(2));
    }

    [Test]
    public void ConstructorInjection_IEnumerable_InChildReceivesParentThenChildRegistrations()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddSingleton<IMyService, MyServiceImpl>();
        parentCollection.AddSingleton<IMyService, AnotherServiceImpl>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<IMyService, AnotherServiceImpl>();
        childCollection.AddSingleton<ServiceWithEnumerableDependency>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        ServiceWithEnumerableDependency service = child.GetRequiredService<ServiceWithEnumerableDependency>();
        IMyService[] services = service.Services.ToArray();

        Assert.That(services, Has.Length.EqualTo(3));
        Assert.That(services[0], Is.InstanceOf<MyServiceImpl>());
        Assert.That(services[1], Is.InstanceOf<AnotherServiceImpl>());
        Assert.That(services[2], Is.InstanceOf<AnotherServiceImpl>());
    }
}

public class FactoryProduct
{
    public string Value { get; }

    public FactoryProduct(string value)
    {
        Value = value;
    }
}

public class FactoryProductWithDependency
{
    public SimpleService Simple { get; }

    public FactoryProductWithDependency(SimpleService simple)
    {
        Simple = simple;
    }
}

public class TestFactory
{
    internal FactoryProduct CreateProduct()
    {
        return new FactoryProduct("from-factory");
    }

    internal FactoryProductWithDependency CreateProductWithDependency(SimpleService simple)
    {
        return new FactoryProductWithDependency(simple);
    }
}

public class BaseProduct
{
    public string Source { get; }

    public BaseProduct(string source)
    {
        Source = source;
    }
}

public class BaseFactory
{
    internal BaseProduct CreateBaseProduct()
    {
        return new BaseProduct("from-base");
    }
}

public class DerivedFactory : BaseFactory
{
}

public class ServiceNeedingProvider
{
    public ServiceProvider Provider { get; }

    public ServiceNeedingProvider(ServiceProvider provider)
    {
        Provider = provider;
    }
}

public interface IDisposableAlias;

public class TrackingDisposableA : IDisposable
{
    private readonly List<string> _log;

    public TrackingDisposableA(List<string> log)
    {
        _log = log;
    }

    public void Dispose()
    {
        _log.Add("A");
    }
}

public class TrackingDisposableB : IDisposable
{
    private readonly List<string> _log;

    public TrackingDisposableB(List<string> log)
    {
        _log = log;
    }

    public void Dispose()
    {
        _log.Add("B");
    }
}

public class TrackingDisposableC : IDisposable
{
    private readonly List<string> _log;

    public TrackingDisposableC(List<string> log)
    {
        _log = log;
    }

    public void Dispose()
    {
        _log.Add("C");
    }
}

public class CountingDisposable : IDisposable, IDisposableAlias
{
    public int DisposeCallCount { get; private set; }

    public void Dispose()
    {
        DisposeCallCount++;
    }
}

public class DisposableOrderTracker : IDisposable
{
    private readonly List<string> _log;
    private readonly string _name;

    public DisposableOrderTracker(List<string> log, string name)
    {
        _log = log;
        _name = name;
    }

    public void Dispose()
    {
        _log.Add(_name);
    }
}

public interface IDisposableFoo;

public class DisposableFooImpl : IDisposableFoo, IDisposable
{
    public bool Disposed { get; private set; }

    public void Dispose()
    {
        Disposed = true;
    }
}

public class ServiceWithEnumerableDependency
{
    public IEnumerable<IMyService> Services { get; }

    public ServiceWithEnumerableDependency(IEnumerable<IMyService> services)
    {
        Services = services;
    }
}

// Helper for OnStart tests that need access to the ServiceProvider inside the callback.
// Registered as a singleton so OnStart can receive it as an injected parameter.
public class OnStartCapturer
{
    public ServiceProvider? CapturedProvider { get; private set; }

    public void Capture(ServiceProvider sp)
    {
        CapturedProvider = sp;
    }

    public IReadOnlyList<T>? CapturedServices<T>() where T : class
    {
        return CapturedProvider?.GetServices<T>();
    }
}
