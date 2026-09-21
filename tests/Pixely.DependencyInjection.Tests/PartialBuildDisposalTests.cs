using Pixely.DependencyInjection;

namespace Pixely.DependencyInjection.Tests;

public class PartialBuildDisposalTests
{
    [Test]
    public void BuildServiceProvider_WhenBuildThrows_DisposesPartiallyCreatedServices()
    {
        ServiceProvider? capturedProvider = null;
        ServiceA? capturedServiceA = null;
        using ServiceProvider parent = new ServiceCollection().BuildServiceProvider();

        ServiceCollection collection = parent.CreateServiceCollection();
        collection.AddSingleton<ServiceA>((ServiceProvider sp) =>
        {
            capturedProvider = sp;
            ServiceA instance = new();
            capturedServiceA = instance;
            return instance;
        });
        collection.AddSingleton<ServiceB>((ServiceProvider sp) =>
        {
            sp.GetRequiredService<ServiceA>();
            throw new InvalidOperationException("boom");
        });

        Assert.Throws<InvalidOperationException>(() => collection.BuildServiceProvider());

        Assert.That(capturedProvider, Is.Not.Null);
        Assert.That(capturedServiceA, Is.Not.Null);
        Assert.That(capturedServiceA!.Disposed, Is.True);
        Assert.Throws<ObjectDisposedException>(() => capturedProvider!.GetRequiredService<ServiceA>());

        parent.Dispose();

        Assert.That(capturedServiceA.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public void BuildServiceProvider_WhenBuildAndCleanupThrow_SurfacesBothWithBuildExceptionFirst()
    {
        List<string> events = new();
        ServiceCollection collection = new();
        collection.AddSingleton(new ThrowingDisposable(events, "cleanup"));
        collection.AddSingleton<ServiceB>((ServiceProvider sp) => throw new InvalidOperationException("build"));

        AggregateException exception = Assert.Throws<AggregateException>(() => collection.BuildServiceProvider());

        Assert.That(events, Is.EqualTo(new[] { "cleanup" }));
        Assert.That(exception.InnerExceptions, Has.Count.EqualTo(2));
        Assert.That(exception.InnerExceptions[0], Is.InstanceOf<InvalidOperationException>().With.Message.EqualTo("build"));
        Assert.That(exception.InnerExceptions[1], Is.InstanceOf<AggregateException>());
        Assert.That(((AggregateException)exception.InnerExceptions[1]).InnerExceptions[0].Message, Is.EqualTo("cleanup"));
    }

    [Test]
    public void BuildServiceProvider_WhenBuildThrowsAndCleanupSucceeds_RethrowsTheBuildExceptionUnchanged()
    {
        InvalidOperationException buildException = new("build");
        ServiceCollection collection = new();
        collection.AddSingleton(new ServiceA());
        collection.AddSingleton<ServiceB>((ServiceProvider sp) => throw buildException);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => collection.BuildServiceProvider());

        Assert.That(exception, Is.SameAs(buildException));
    }

    private class ServiceA : IDisposable
    {
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }

    private class ServiceB
    {
    }
}
