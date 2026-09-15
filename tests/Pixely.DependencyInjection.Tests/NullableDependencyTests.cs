using Pixely.DependencyInjection;

namespace Pixely.DependencyInjection.Tests;

public sealed class NullableConstructorConsumer
{
    public NullableConstructorConsumer(SimpleService? service)
    {
        Service = service;
    }

    public SimpleService? Service { get; }
}

public sealed class NullableTransientConsumer
{
    public NullableTransientConsumer(SimpleService? service)
    {
        Service = service;
    }

    public SimpleService? Service { get; }
}

public sealed class NullableDelegateProduct
{
    public NullableDelegateProduct(SimpleService? service)
    {
        Service = service;
    }

    public SimpleService? Service { get; }
}

public sealed class NullableFactoryProduct
{
    public NullableFactoryProduct(SimpleService? service)
    {
        Service = service;
    }

    public SimpleService? Service { get; }
}

public sealed class NullableDependencyFactory
{
    internal NullableFactoryProduct Create(SimpleService? service)
    {
        return new NullableFactoryProduct(service);
    }
}

public sealed class NullableEnumerableConsumer
{
    public NullableEnumerableConsumer(IEnumerable<SimpleService?>? services)
    {
        Services = services;
    }

    public IEnumerable<SimpleService?>? Services { get; }
}

public sealed class OptionalCycleA
{
    public OptionalCycleA(OptionalCycleB? service)
    {
        Service = service;
    }

    public OptionalCycleB? Service { get; }
}

public sealed class OptionalCycleB
{
    public OptionalCycleB(OptionalCycleA service)
    {
        Service = service;
    }

    public OptionalCycleA Service { get; }
}

public sealed class NullableDependencyTests
{
    [Test]
    public void SingletonConstructor_MissingNullableDependency_ReceivesNull()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<NullableConstructorConsumer>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<NullableConstructorConsumer>().Service, Is.Null);
    }

    [Test]
    public void SingletonConstructor_RegisteredNullableDependency_ResolvesDependency()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<NullableConstructorConsumer>();
        collection.AddSingleton<SimpleService>();

        ServiceProvider provider = collection.BuildServiceProvider();

        NullableConstructorConsumer consumer = provider.GetRequiredService<NullableConstructorConsumer>();
        Assert.That(consumer.Service, Is.SameAs(provider.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void DelegateFactory_MissingNullableDependency_ReceivesNull()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<NullableDelegateProduct>(
            (SimpleService? service) => new NullableDelegateProduct(service));

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<NullableDelegateProduct>().Service, Is.Null);
    }

    [Test]
    public void InstanceFactory_MissingNullableDependency_ReceivesNull()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<NullableDependencyFactory>();
        collection.AddSingleton<NullableFactoryProduct, NullableDependencyFactory>();

        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<NullableFactoryProduct>().Service, Is.Null);
    }

    [Test]
    public void ParentTransient_ResolvedFromChild_UsesChildNullableDependency()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddTransient<NullableTransientConsumer>();
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<SimpleService>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        NullableTransientConsumer consumer = child.GetRequiredService<NullableTransientConsumer>();

        Assert.That(consumer.Service, Is.SameAs(child.GetRequiredService<SimpleService>()));
    }

    [Test]
    public void Transient_MissingNullableDependencyAfterFreeze_ReceivesNull()
    {
        ServiceCollection collection = new();
        collection.AddTransient<NullableTransientConsumer>();
        ServiceProvider provider = collection.BuildServiceProvider();

        NullableTransientConsumer consumer = provider.GetRequiredService<NullableTransientConsumer>();

        Assert.That(consumer.Service, Is.Null);
    }

    [Test]
    public void OnBuilt_MissingNullableDependency_ReceivesNull()
    {
        SimpleService? captured = new();
        ServiceCollection collection = new();
        collection.OnBuilt((SimpleService? service) => captured = service);

        collection.BuildServiceProvider();

        Assert.That(captured, Is.Null);
    }

    [Test]
    public void NullableEnumerableDependency_MissingRegistrations_ReceivesEmptyCollection()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<NullableEnumerableConsumer>();

        ServiceProvider provider = collection.BuildServiceProvider();

        NullableEnumerableConsumer consumer = provider.GetRequiredService<NullableEnumerableConsumer>();
        Assert.That(consumer.Services, Is.Not.Null);
        Assert.That(consumer.Services, Is.Empty);
    }

    [Test]
    public void NullableDependency_CircularRegistration_StillThrows()
    {
        ServiceCollection collection = new();
        collection.AddSingleton<OptionalCycleA>();
        collection.AddSingleton<OptionalCycleB>();

        Assert.Throws<InvalidOperationException>(() => collection.BuildServiceProvider());
    }
}
