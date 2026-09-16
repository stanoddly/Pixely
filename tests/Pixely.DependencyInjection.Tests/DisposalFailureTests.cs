using System.Runtime.CompilerServices;
using Pixely.DependencyInjection;

namespace Pixely.DependencyInjection.Tests;

public sealed class RecordingDisposable : IDisposable
{
    private readonly List<string> _events;
    private readonly string _name;

    public RecordingDisposable(List<string> events, string name)
    {
        _events = events;
        _name = name;
    }

    public void Dispose()
    {
        _events.Add(_name);
    }
}

public sealed class ThrowingDisposable : IDisposable
{
    private readonly List<string> _events;
    private readonly string _name;

    public ThrowingDisposable(List<string> events, string name)
    {
        _events = events;
        _name = name;
    }

    public void Dispose()
    {
        _events.Add(_name);
        throw new InvalidOperationException(_name);
    }
}

public sealed class PlainService;

public sealed class DisposalFailureTests
{
    [Test]
    public void Dispose_WhenServiceThrows_DisposesServicesCreatedBeforeIt()
    {
        List<string> events = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new RecordingDisposable(events, "first"));
        collection.AddSingleton(new RecordingDisposable(events, "second"));
        collection.AddSingleton(new ThrowingDisposable(events, "throwing"));
        ServiceProvider provider = collection.BuildServiceProvider();

        AggregateException exception = Assert.Throws<AggregateException>(provider.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "throwing", "second", "first" }));
        Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(exception.InnerExceptions[0], Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo("throwing"));
    }

    [Test]
    public void Dispose_WhenTransientThrows_DisposesRemainingTransientsAndSingletons()
    {
        List<string> events = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new RecordingDisposable(events, "singleton"));
        collection.AddTransient<ThrowingDisposable>(sp => new ThrowingDisposable(events, "transient"));
        ServiceProvider provider = collection.BuildServiceProvider();
        provider.GetRequiredService<ThrowingDisposable>();
        provider.GetRequiredService<ThrowingDisposable>();

        AggregateException exception = Assert.Throws<AggregateException>(provider.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "transient", "transient", "singleton" }));
        Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
    }

    [Test]
    public void Dispose_WhenChildThrows_DisposesRemainingChildrenAndOwnServices()
    {
        List<string> events = new();
        ServiceCollection parentCollection = new();
        parentCollection.AddSingleton(new RecordingDisposable(events, "parent"));
        ServiceProvider parent = parentCollection.BuildServiceProvider();

        ServiceCollection firstChildCollection = parent.CreateServiceCollection();
        firstChildCollection.AddSingleton(new RecordingDisposable(events, "first child"));
        ServiceProvider firstChild = firstChildCollection.BuildServiceProvider();

        ServiceCollection secondChildCollection = parent.CreateServiceCollection();
        secondChildCollection.AddSingleton(new ThrowingDisposable(events, "second child"));
        ServiceProvider secondChild = secondChildCollection.BuildServiceProvider();

        AggregateException exception = Assert.Throws<AggregateException>(parent.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "second child", "first child", "parent" }));
        Assert.That(exception.InnerExceptions, Has.Count.EqualTo(1));
        Assert.That(exception.InnerExceptions[0], Is.InstanceOf<AggregateException>());
        Assert.Throws<ObjectDisposedException>(() => firstChild.GetRequiredService<RecordingDisposable>());
        Assert.Throws<ObjectDisposedException>(() => secondChild.GetRequiredService<ThrowingDisposable>());
    }

    [Test]
    public void Dispose_WhenDisposingCallbackThrows_StillDisposesTheServiceAndTheRest()
    {
        List<string> events = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new RecordingDisposable(events, "first"));
        collection.AddSingleton(new RecordingDisposable(events, "second"));
        collection.OnDisposing((instance, type) =>
        {
            events.Add("callback");
            throw new InvalidOperationException("callback");
        });
        ServiceProvider provider = collection.BuildServiceProvider();

        AggregateException exception = Assert.Throws<AggregateException>(provider.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "callback", "second", "callback", "first" }));
        Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
    }

    [Test]
    public void Dispose_WhenMultipleServicesThrow_ThrowsOneExceptionCarryingAll()
    {
        List<string> events = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new RecordingDisposable(events, "recording"));
        collection.AddSingleton(new ThrowingDisposable(events, "first"));
        collection.AddSingleton(new ThrowingDisposable(events, "second"));
        ServiceProvider provider = collection.BuildServiceProvider();

        AggregateException exception = Assert.Throws<AggregateException>(provider.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "second", "first", "recording" }));
        Assert.That(exception.InnerExceptions.Select(e => e.Message), Is.EqualTo(new[] { "second", "first" }));
    }

    [Test]
    public void Dispose_WhenServiceThrows_StillMarksProviderDisposedAndDoesNotRetry()
    {
        List<string> events = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new ThrowingDisposable(events, "throwing"));
        ServiceProvider provider = collection.BuildServiceProvider();

        Assert.Throws<AggregateException>(provider.Dispose);
        Assert.DoesNotThrow(provider.Dispose);

        Assert.That(events, Is.EqualTo(new[] { "throwing" }));
        Assert.Throws<ObjectDisposedException>(() => provider.GetRequiredService<ThrowingDisposable>());
    }

    [Test]
    public void Dispose_WhenServiceThrows_ReleasesTheOtherServices()
    {
        ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        WeakReference plainService = BuildProviderWithThrowingService(out provider);

        Assert.Throws<AggregateException>(provider.Dispose);
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        Assert.That(plainService.IsAlive, Is.False);
        GC.KeepAlive(provider);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference BuildProviderWithThrowingService(out ServiceProvider provider)
    {
        PlainService plainService = new();
        ServiceCollection collection = new();
        collection.AddSingleton(plainService);
        collection.AddSingleton(new ThrowingDisposable(new List<string>(), "throwing"));
        provider = collection.BuildServiceProvider();
        return new WeakReference(plainService);
    }
}
