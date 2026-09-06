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
    public void AViewBuiltAfterTheRegistry_ReachesTheRootForItsScope()
    {
        UiRoot first = new();
        UiRoot second = new();
        ServiceCollection services = Configured(first, second, out UiViewRegistry registry);
        services.AddSingleton<ScopedView>(_ => new ScopedView(new ViewScope(2)));

        using ServiceProvider provider = services.BuildServiceProvider();
        registry.Bind(provider);
        ScopedView view = provider.GetRequiredService<ScopedView>();

        Assert.Multiple(() =>
        {
            Assert.That(second.Layers, Does.Contain(view.Root));
            Assert.That(first.Layers, Is.Empty, "a view goes to the window it named and no other");
        });
    }

    [Test]
    public void AViewBuiltBeforeTheRegistryIsBound_IsNotDropped()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, root, out UiViewRegistry registry);
        services.AddSingleton<ScopedView>(_ => new ScopedView(default));

        using ServiceProvider provider = services.BuildServiceProvider();
        ScopedView view = provider.GetRequiredService<ScopedView>();

        // Built first, so there was no provider to resolve a root from at the time.
        registry.Bind(provider);

        Assert.That(root.Layers, Does.Contain(view.Root));
    }

    [Test]
    public void DisposingTheProvider_TakesViewsOffTheirRoot()
    {
        UiRoot root = new();
        ServiceCollection services = Configured(root, root, out UiViewRegistry registry);
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
        ServiceCollection services = Configured(root, root, out UiViewRegistry registry);
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

    internal sealed class NestingView : ScopedView, IDisposable
    {
        private readonly Func<UiView> _buildAnother;

        public NestingView(Func<UiView> buildAnother) : base(default) => _buildAnother = buildAnother;

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
        ServiceCollection services = Configured(root, root, out UiViewRegistry registry);
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

    internal sealed class SelfDisposingView : ScopedView, IDisposable
    {
        private readonly Action _onBuild;

        public SelfDisposingView(Action onBuild) : base(default) => _onBuild = onBuild;

        public void Dispose()
        {
        }

        protected override Element BuildRoot()
        {
            _onBuild();
            return new Column();
        }
    }

    private static ServiceCollection Configured(UiRoot first, UiRoot second, out UiViewRegistry registry)
    {
        ServiceCollection services = new();
        services.AddRegistry<ScopedUiRoot>();
        services.AddSingleton(new ScopedUiRoot(default, first));
        services.AddSingleton(new ScopedUiRoot(new ViewScope(2), second));
        registry = UiViewRegistry.Register(services);
        return services;
    }

    internal class ScopedView : UiView
    {
        private readonly ViewScope _viewScope;

        public ScopedView(ViewScope viewScope) => _viewScope = viewScope;

        public override ViewScope ViewScope => _viewScope;

        protected override Element BuildRoot() => new Column();

        protected override void Synchronize()
        {
        }
    }

    /// <summary>Disposable so the container keeps hold of it and tears it down with the provider.</summary>
    internal sealed class DisposableView : ScopedView, IDisposable
    {
        public DisposableView() : base(default)
        {
        }

        public void Dispose()
        {
        }
    }
}
