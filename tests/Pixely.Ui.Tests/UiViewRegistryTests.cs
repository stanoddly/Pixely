using Pixely.DependencyInjection;

namespace Pixely.Ui.Tests;

/// <summary>
/// Views reaching their root through the container rather than by hand, which is what makes
/// registering one a single line. Built on a real container, because the ordering these have to
/// survive — a view built before the registry, another built while it is catching up, and the whole
/// lot torn down — is the container's decision, not something a stand-in would reproduce.
/// </summary>
public class UiViewRegistryTests
{
    [Test]
    public void AViewBuiltAfterTheRegistry_ReachesTheRoot()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, out UiViewRegistry registry);
        services.AddSingleton<PlainView>(_ => new PlainView());

        using ServiceProvider provider = services.BuildServiceProvider();
        registry.Bind(provider);
        PlainView view = provider.GetRequiredService<PlainView>();

        Assert.That(root.Layers, Does.Contain(view.Root));
    }

    [Test]
    public void AViewBuiltBeforeTheRegistryIsBound_IsNotDropped()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, out UiViewRegistry registry);
        services.AddSingleton<PlainView>(_ => new PlainView());

        using ServiceProvider provider = services.BuildServiceProvider();
        PlainView view = provider.GetRequiredService<PlainView>();

        // Built first, so there was no provider to resolve a root from at the time.
        registry.Bind(provider);

        Assert.That(root.Layers, Does.Contain(view.Root));
    }

    [Test]
    public void DisposingTheProvider_TakesViewsOffTheirRoot()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, out UiViewRegistry registry);
        services.AddSingleton<DisposableView>(_ => new DisposableView());

        DisposableView view;
        ServiceProvider provider = services.BuildServiceProvider();

        try
        {
            registry.Bind(provider);
            view = provider.GetRequiredService<DisposableView>();
            Assert.That(root.Layers, Does.Contain(view.Root));
        }
        finally
        {
            // The provider marks itself disposed before it says so, which is why the root a view went
            // to has to be remembered rather than looked up again here.
            provider.Dispose();
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Layers, Is.Empty);
            Assert.That(view.IsAttached, Is.False);
        });
    }

    [Test]
    public void AViewBuiltWhileAnotherIsAttaching_IsStillTrackedSeparately()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, out UiViewRegistry registry);
        // Both transient, so they are built when asked for rather than up front with the singletons.
        // That is what puts one activation inside the other.
        services.AddTransient<DisposableView>(() => new DisposableView());
        services.AddTransient<NestingView>((ServiceProvider p) => new NestingView(() => p.GetRequiredService<DisposableView>()));

        ServiceProvider provider = services.BuildServiceProvider();
        NestingView outer;

        try
        {
            registry.Bind(provider);
            outer = provider.GetRequiredService<NestingView>();

            Assert.That(root.Layers, Has.Count.EqualTo(2), "both are on the root");
        }
        finally
        {
            provider.Dispose();
        }

        Assert.Multiple(() =>
        {
            Assert.That(root.Layers, Is.Empty, "and both come off it again");
            Assert.That(outer.IsAttached, Is.False);
        });
    }

    internal sealed class NestingView : PlainView, IDisposable
    {
        private readonly Func<UiView> _buildAnother;

        public NestingView(Func<UiView> buildAnother) => _buildAnother = buildAnother;

        public void Dispose()
        {
        }

        protected override Element BuildRoot()
        {
            _buildAnother();
            return new Column();
        }
    }

    [Test]
    public void AViewTakenAwayWhileItIsAttaching_DoesNotStayOnTheRoot()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, out UiViewRegistry registry);
        ServiceProvider? built = null;
        services.AddTransient<SelfDisposingView>(() => new SelfDisposingView(() => built!.Dispose()));

        built = services.BuildServiceProvider();
        registry.Bind(built);

        // The container tracks a disposable transient before it announces it, so taking the container
        // down from inside this view's own attach removes its entry while the attach is still going.
        SelfDisposingView view = built.GetRequiredService<SelfDisposingView>();

        Assert.Multiple(() =>
        {
            Assert.That(root.Layers, Is.Empty, "or it is left on a root with nothing able to take it off");
            Assert.That(view.IsAttached, Is.False);
        });
    }

    internal sealed class SelfDisposingView : PlainView, IDisposable
    {
        private readonly Action _onBuild;

        public SelfDisposingView(Action onBuild) => _onBuild = onBuild;

        public void Dispose()
        {
        }

        protected override Element BuildRoot()
        {
            _onBuild();
            return new Column();
        }
    }

    private static ServiceCollection Configured(UiRoot root, out UiViewRegistry registry)
    {
        ServiceCollection services = new();
        services.AddSingleton(root);
        registry = UiViewRegistry.Register(services);
        return services;
    }

    internal class PlainView : UiView
    {
        protected override Element BuildRoot() => new Column();

        protected override void Synchronize()
        {
        }
    }

    /// <summary>Disposable so the container keeps hold of it and tears it down with the provider.</summary>
    internal sealed class DisposableView : PlainView, IDisposable
    {
        public void Dispose()
        {
        }
    }
}
