using System.Diagnostics.CodeAnalysis;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;

namespace Pixely.Tests;

public class RenderCoordinatorTests
{
    [Test]
    public void Builder_DoesNotRegisterWindowWithoutWindowRendering()
    {
        PixelyAppBuilder builder = new();

        Assert.That(builder.IsRegistered<Window>(), Is.False);
    }

    [Test]
    public void UseDefaultRendering_RegistersWindow()
    {
        PixelyAppBuilder builder = new();

        builder.UseDefaultRendering();

        Assert.That(builder.IsRegistered<Window>(), Is.True);
    }

    [Test]
    public void AddWindow_RegistersWindowWithoutRenderCoordinator()
    {
        PixelyAppBuilder builder = new();

        builder.AddWindow();

        Assert.Multiple(() =>
        {
            Assert.That(builder.IsRegistered<Window>(), Is.True);
            Assert.That(builder.IsRegistered<IRenderCoordinator>(), Is.False);
        });
    }

    [Test]
    public void Renderer_WithoutExplicitViewScope_UsesDefaultScope()
    {
        IRenderer<TestRenderContext> renderer =
            new TestRenderer("renderer", new List<string>());

        Assert.That(renderer.ViewScope, Is.EqualTo(default(ViewScope)));
    }

    [Test]
    public void Execute_WithNoRenderers_DoesNotThrow()
    {
        PixelyAppBuilder builder = CreateBuilder(new List<string>());
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        Assert.DoesNotThrow(renderCoordinator.Execute);
    }

    [Test]
    public void Execute_WhenRenderContextCannotBeCreated_DoesNotRender()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new() { CanCreate = false };
        PixelyAppBuilder builder = CreateBuilder(calls, renderContextSource);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls));
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        renderCoordinator.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.Empty);
            Assert.That(renderContextSource.LastRenderContext, Is.Null);
        });
    }

    [Test]
    public void Execute_WithRenderContext_DisposesRenderContext()
    {
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder builder = CreateBuilder(new List<string>(), renderContextSource);
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        renderCoordinator.Execute();

        Assert.That(renderContextSource.LastRenderContext?.IsDisposed, Is.True);
    }

    [Test]
    public void ChildProviderRenderer_IsRenderedAfterChildBuild()
    {
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls);
        ServiceProvider parent = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = parent.GetRequiredService<IRenderCoordinator>();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("child", calls));
        using ServiceProvider child = childCollection.BuildServiceProvider();

        renderCoordinator.Execute();

        Assert.That(calls, Is.EqualTo(new[] { "child" }));
    }

    [Test]
    public void ChildProviderRenderer_IsRemovedWhenChildProviderIsDisposed()
    {
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls);
        ServiceProvider parent = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = parent.GetRequiredService<IRenderCoordinator>();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("child", calls));
        ServiceProvider child = childCollection.BuildServiceProvider();

        child.Dispose();
        renderCoordinator.Execute();

        Assert.That(calls, Is.Empty);
    }

    [Test]
    public void DynamicRenderers_AreRenderedInOrder()
    {
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls, 10));
        ServiceProvider parent = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = parent.GetRequiredService<IRenderCoordinator>();

        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("child", calls, 5));
        using ServiceProvider child = childCollection.BuildServiceProvider();

        renderCoordinator.Execute();

        Assert.That(calls, Is.EqualTo(new[] { "child", "root" }));
    }

    [Test]
    public void ChildProviderDisposeDuringRender_DoesNotSkipRemainingRootRenderers()
    {
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls, 10));
        ServiceProvider parent = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = parent.GetRequiredService<IRenderCoordinator>();

        ServiceProvider? child = null;
        ServiceCollection childCollection = parent.CreateServiceCollection();
        childCollection.AddSingleton<IRenderer<TestRenderContext>>(new DisposingRenderer("child", calls, () => child!, 0));
        child = childCollection.BuildServiceProvider();

        renderCoordinator.Execute();

        Assert.That(calls, Is.EqualTo(new[] { "child", "root" }));
    }

    [Test]
    public void ChildProviderBuildDuringRender_AddsRendererForNextFrame()
    {
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls);
        ServiceProvider? parent = null;
        builder.AddSingleton<IRenderer<TestRenderContext>>(new ChildProviderBuildingRenderer("root", calls, () => parent!, 0));
        parent = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = parent.GetRequiredService<IRenderCoordinator>();

        renderCoordinator.Execute();

        Assert.That(calls, Is.EqualTo(new[] { "root" }));

        calls.Clear();
        renderCoordinator.Execute();

        Assert.That(calls, Is.EqualTo(new[] { "child", "root" }));
    }

    private static PixelyAppBuilder CreateBuilder(
        List<string> calls,
        TestRenderContextSource? renderContextSource = null)
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(renderContextSource ?? new TestRenderContextSource());
        builder.UseRenderCoordinator<TestRenderContext>(
            static (provider, renderers) => new TestRenderCoordinator(
                provider.GetRequiredService<GpuMemorySystem>(),
                renderers,
                provider.GetRequiredService<TestRenderContextSource>()));
        builder.AddSingleton(new GpuMemorySystem(null!));
        builder.AddSingleton(calls);
        return builder;
    }

    private sealed class TestRenderCoordinator : RenderCoordinator<TestRenderContext>
    {
        private readonly TestRenderContextSource _renderContextSource;

        public TestRenderCoordinator(
            GpuMemorySystem gpuMemorySystem,
            ServiceRegistry<IRenderer<TestRenderContext>> renderers,
            TestRenderContextSource renderContextSource)
            : base(gpuMemorySystem, renderers)
        {
            _renderContextSource = renderContextSource;
        }

        protected override bool TryCreateRenderContext(
            [NotNullWhen(true)] out TestRenderContext? renderContext)
        {
            if (!_renderContextSource.CanCreate)
            {
                renderContext = null;
                return false;
            }

            renderContext = new TestRenderContext();
            _renderContextSource.LastRenderContext = renderContext;
            return true;
        }
    }

    private sealed class TestRenderContextSource
    {
        public bool CanCreate { get; init; } = true;
        public TestRenderContext? LastRenderContext { get; set; }
    }

    private sealed class TestRenderContext : IRenderContext
    {
        public CommandBuffer CommandBuffer => null!;

        public Texture ColorTarget => null!;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private class TestRenderer : IRenderer<TestRenderContext>
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public TestRenderer(string name, List<string> calls, int order = 0)
        {
            _name = name;
            _calls = calls;
            Order = order;
        }

        protected List<string> Calls => _calls;

        public int Order { get; }

        public virtual void Render(TestRenderContext renderContext)
        {
            _calls.Add(_name);
        }
    }

    private sealed class DisposingRenderer : TestRenderer
    {
        private readonly Func<ServiceProvider> _provider;

        public DisposingRenderer(
            string name,
            List<string> calls,
            Func<ServiceProvider> provider,
            int order)
            : base(name, calls, order)
        {
            _provider = provider;
        }

        public override void Render(TestRenderContext renderContext)
        {
            base.Render(renderContext);
            _provider().Dispose();
        }
    }

    private sealed class ChildProviderBuildingRenderer : TestRenderer
    {
        private readonly Func<ServiceProvider> _parentProvider;
        private ServiceProvider? _child;

        public ChildProviderBuildingRenderer(
            string name,
            List<string> calls,
            Func<ServiceProvider> parentProvider,
            int order)
            : base(name, calls, order)
        {
            _parentProvider = parentProvider;
        }

        public override void Render(TestRenderContext renderContext)
        {
            base.Render(renderContext);

            if (_child != null)
            {
                return;
            }

            ServiceProvider parent = _parentProvider();
            ServiceCollection childCollection = parent.CreateServiceCollection();
            childCollection.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("child", Calls, -10));
            _child = childCollection.BuildServiceProvider();
        }
    }
}
