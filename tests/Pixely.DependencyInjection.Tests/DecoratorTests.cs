using Pixely.DependencyInjection;

namespace Pixely.DependencyInjection.Tests;

public interface IDecoratedContract;

public sealed class DecoratedService : IDecoratedContract
{
    public DecoratedService(string name = "original")
    {
        Name = name;
    }

    public string Name { get; }
}

public sealed class UndecoratedService;

public sealed class DecoratedConsumer
{
    public DecoratedConsumer(DecoratedService decorated)
    {
        Decorated = decorated;
    }

    public DecoratedService Decorated { get; }
}

public sealed class DecoratedTransientService
{
    public int Generation { get; init; }
}

public sealed class DecoratorTests
{
    [Test]
    public void Decorate_ReplacesInstance_ForConsumersAndResolution()
    {
        ServiceCollection collection = new();
        collection.AddSingleton(new DecoratedService());
        collection.AddSingleton<DecoratedConsumer>();
        collection.Decorate<DecoratedService>(service => new DecoratedService(service.Name + "+decorated"));

        using ServiceProvider provider = collection.BuildServiceProvider();
        DecoratedService resolved = provider.GetRequiredService<DecoratedService>();

        Assert.That(resolved.Name, Is.EqualTo("original+decorated"));
        Assert.That(provider.GetRequiredService<DecoratedConsumer>().Decorated, Is.SameAs(resolved));
        Assert.That(provider.GetServices<DecoratedService>(), Is.EqualTo(new[] { resolved }));
    }

    [Test]
    public void Decorate_ReturningSameInstance_KeepsIt()
    {
        DecoratedService original = new();
        ServiceCollection collection = new();
        collection.AddSingleton(original);
        collection.Decorate<DecoratedService>(service => service);

        using ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<DecoratedService>(), Is.SameAs(original));
    }

    [Test]
    public void Decorate_RunsInRegistrationOrder_AndOnlyForItsType()
    {
        ServiceCollection collection = new();
        collection.Decorate<DecoratedService>(service => new DecoratedService(service.Name + "+first"));
        collection.AddSingleton(new DecoratedService());
        collection.AddSingleton<UndecoratedService>();
        collection.Decorate<DecoratedService>(service => new DecoratedService(service.Name + "+second"));
        int undecoratedActivations = 0;
        collection.OnActivated((instance, _) => undecoratedActivations += instance is UndecoratedService ? 1 : 0);

        using ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<DecoratedService>().Name, Is.EqualTo("original+first+second"));
        Assert.That(undecoratedActivations, Is.EqualTo(1));
    }

    [Test]
    public void Decorate_RunsBeforeActivation_AndAliasesSeeTheReplacement()
    {
        List<object> activated = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new DecoratedService());
        collection.AddAlias<IDecoratedContract, DecoratedService>();
        collection.Decorate<DecoratedService>(service => new DecoratedService("replacement"));
        collection.OnActivated((instance, _) => activated.Add(instance));

        using ServiceProvider provider = collection.BuildServiceProvider();
        DecoratedService resolved = provider.GetRequiredService<DecoratedService>();

        Assert.That(resolved.Name, Is.EqualTo("replacement"));
        Assert.That(activated, Is.EqualTo(new object[] { resolved }));
        Assert.That(provider.GetRequiredService<IDecoratedContract>(), Is.SameAs(resolved));
    }

    [Test]
    public void Decorate_DoesNotApplyToAliasType()
    {
        ServiceCollection collection = new();
        collection.AddSingleton(new DecoratedService());
        collection.AddAlias<IDecoratedContract, DecoratedService>();
        collection.Decorate<IDecoratedContract>(_ => new DecoratedService("alias"));

        using ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IDecoratedContract>(), Is.SameAs(provider.GetRequiredService<DecoratedService>()));
    }

    [Test]
    public void Decorate_AppliesToEveryTransientInstance()
    {
        int generation = 0;
        ServiceCollection collection = new();
        collection.AddTransient<DecoratedTransientService>(static sp => new DecoratedTransientService());
        collection.Decorate<DecoratedTransientService>(_ => new DecoratedTransientService { Generation = ++generation });

        using ServiceProvider provider = collection.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<DecoratedTransientService>().Generation, Is.EqualTo(1));
        Assert.That(provider.GetRequiredService<DecoratedTransientService>().Generation, Is.EqualTo(2));
    }

    [Test]
    public void Decorate_ChildProvider_RunsParentDecoratorsFirst()
    {
        ServiceCollection parentCollection = new();
        parentCollection.AddSingleton<UndecoratedService>();
        parentCollection.Decorate<DecoratedService>(service => new DecoratedService(service.Name + "+parent"));
        using ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton(new DecoratedService());
        childCollection.Decorate<DecoratedService>(service => new DecoratedService(service.Name + "+child"));
        using ServiceProvider child = childCollection.BuildServiceProvider();

        Assert.That(child.GetRequiredService<DecoratedService>().Name, Is.EqualTo("original+parent+child"));
    }

    [Test]
    public void Decorate_ReturningNull_Throws()
    {
        ServiceCollection collection = new();
        collection.AddSingleton(new DecoratedService());
        collection.Decorate<DecoratedService>(_ => null!);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => collection.BuildServiceProvider())!;

        Assert.That(exception.Message, Does.Contain(nameof(DecoratedService)));
    }
}
