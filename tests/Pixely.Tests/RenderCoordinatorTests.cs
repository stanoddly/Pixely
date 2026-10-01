using System.Reflection;
using System.Runtime.CompilerServices;
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
    public void UseDefaultRendering_RegistersWindowAndBasicRenderContextProvider()
    {
        PixelyAppBuilder builder = new();

        builder.UseDefaultRendering();

        Assert.Multiple(() =>
        {
            Assert.That(builder.IsRegistered<Window>(), Is.True);
            Assert.That(builder.IsRegistered<RenderContextProvider<BasicRenderContext>>(), Is.True);
        });
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

        Assert.DoesNotThrow(() => renderCoordinator.Execute());
    }

    [Test]
    public void Execute_WhenFrameCannotBeAcquired_DoesNotCreateContextOrRender()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder builder = CreateBuilder(calls, renderContextSource, canAcquireFrame: false);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls));
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        bool drawn = renderCoordinator.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.False);
            Assert.That(calls, Is.Empty);
            Assert.That(renderContextSource.LastWindow, Is.Null);
        });
    }

    [Test]
    public void Execute_WhenWindowIsNotRenderable_CreatesAndDisposesContextWithoutRendering()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder builder = CreateBuilder(calls, renderContextSource, renderable: false);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls));
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        bool drawn = renderCoordinator.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.False);
            Assert.That(calls, Is.Empty);
            Assert.That(renderContextSource.LastRenderContext?.IsDisposed, Is.True);
        });
    }

    [Test]
    public void Execute_WithRenderableWindow_ReportsDrawn()
    {
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls));
        ServiceProvider provider = builder.BuildServiceProvider();

        bool drawn = provider.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "root" }));
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
    public void Execute_PassesManagedWindowToRenderContextProvider()
    {
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder builder = CreateBuilder(new List<string>(), renderContextSource);
        ServiceProvider provider = builder.BuildServiceProvider();

        provider.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.That(renderContextSource.LastWindow, Is.SameAs(provider.GetRequiredService<Window>()));
    }

    [Test]
    public void Execute_RendersOnlyMatchingViewScope()
    {
        ViewScope viewScope = new(7);
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls, viewScope: viewScope);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("matching", calls, viewScope: viewScope));
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("other", calls));
        ServiceProvider provider = builder.BuildServiceProvider();

        provider.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.That(calls, Is.EqualTo(new[] { "matching" }));
    }

    [Test]
    public void ChildProviderCoordinator_UsesChildWindow()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(CreateGpuDeviceStub());
        builder.AddSingleton(new GpuMemorySystem(null!));
        ServiceProvider parent = builder.BuildServiceProvider();
        ServiceCollection childCollection = parent.CreateServiceCollection();
        ViewScope viewScope = new(7);
        Window window = CreateWindow(viewScope, 42);
        TestRenderContextSource renderContextSource = new();
        childCollection.UseWindowRendering<TestRenderContext>(viewScope);
        childCollection.AddSingleton(window);
        childCollection.AddSingleton(renderContextSource);
        childCollection.AddAlias<RenderContextProvider<TestRenderContext>, TestRenderContextSource>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        child.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.That(renderContextSource.LastWindow, Is.SameAs(window));
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

    [Test]
    public void AppRender_WithoutCoordinators_CountsAsDrawn()
    {
        Assert.That(PixelyApp.Render(CreateCoordinatorRegistry()), Is.True);
    }

    [Test]
    public void AppRender_WhenAnyCoordinatorDraws_CountsAsDrawn()
    {
        Assert.That(PixelyApp.Render(CreateCoordinatorRegistry(false, true)), Is.True);
    }

    [Test]
    public void AppRender_WhenNoCoordinatorDraws_CountsAsUndrawn()
    {
        Assert.That(PixelyApp.Render(CreateCoordinatorRegistry(false, false)), Is.False);
    }

    private static ServiceRegistry<IRenderCoordinator> CreateCoordinatorRegistry(params bool[] drawn)
    {
        PixelyAppBuilder builder = new();
        foreach (bool coordinatorDraws in drawn)
        {
            builder.AddSingleton<IRenderCoordinator>(new StubRenderCoordinator(coordinatorDraws));
        }

        return builder.BuildServiceProvider().GetRequiredService<ServiceRegistry<IRenderCoordinator>>();
    }

    private sealed class StubRenderCoordinator(bool drawn) : IRenderCoordinator
    {
        public bool Execute() => drawn;
    }

    private static PixelyAppBuilder CreateBuilder(
        List<string> calls,
        TestRenderContextSource? renderContextSource = null,
        ViewScope viewScope = default,
        bool renderable = true,
        bool canAcquireFrame = true)
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(CreateGpuDeviceStub());
        builder.UseWindowRendering<TestRenderContext>(viewScope);
        builder.AddSingleton(CreateWindow(viewScope, 42, renderable, canAcquireFrame));
        builder.AddSingleton(renderContextSource ?? new TestRenderContextSource());
        builder.AddAlias<RenderContextProvider<TestRenderContext>, TestRenderContextSource>();
        builder.AddSingleton(new GpuMemorySystem(null!));
        builder.AddSingleton(calls);
        return builder;
    }

    // A registered device keeps UseGpu from registering the real one, which would need SDL. Providers holding the stub are never disposed: it cannot survive GpuDevice.Dispose.
    private static GpuDevice CreateGpuDeviceStub()
    {
        return (GpuDevice)RuntimeHelpers.GetUninitializedObject(typeof(GpuDevice));
    }

    private static Window CreateWindow(ViewScope viewScope, uint sdlId, bool renderable = true, bool canAcquireFrame = true)
    {
        TestWindow window = (TestWindow)RuntimeHelpers.GetUninitializedObject(typeof(TestWindow));
        window.Renderable = renderable;
        window.CanAcquireFrame = canAcquireFrame;
        SetBackingField(window, nameof(Window.ViewScope), viewScope);
        SetBackingField(window, nameof(Window.SdlId), sdlId);
        return window;
    }

    private static void SetBackingField<T>(Window window, string propertyName, T value)
    {
        FieldInfo field = typeof(Window).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, value);
    }

    // Created uninitialised, so it never reaches SDL. The constructor exists only because a derived class must name a base
    // constructor to compile. A frame it acquires has no command buffer or texture: the test contexts never use them.
    private sealed class TestWindow : Window
    {
        private TestWindow()
            : base(default, default, 0, null!, null!, default)
        {
        }

        public bool Renderable { get; set; }

        public bool CanAcquireFrame { get; set; }

        public override bool IsRenderable => Renderable;

        public override TextureFormat ColorTargetFormat => throw new NotSupportedException();

        public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture) => throw new NotSupportedException();

        internal override bool TryAcquireFrame(GpuDevice gpuDevice, out FrameContext frameContext)
        {
            frameContext = new FrameContext { Window = this, CommandBuffer = null!, SwapchainTexture = null! };
            return CanAcquireFrame;
        }
    }

    private sealed class TestRenderContextSource : RenderContextProvider<TestRenderContext>
    {
        public TestRenderContext? LastRenderContext { get; private set; }
        public Window? LastWindow { get; private set; }

        public override TestRenderContext CreateRenderContext(FrameContext frameContext)
        {
            LastWindow = frameContext.Window;
            LastRenderContext = new TestRenderContext();
            return LastRenderContext;
        }
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

        public TestRenderer(string name, List<string> calls, int renderOrder = 0, ViewScope viewScope = default)
        {
            _name = name;
            _calls = calls;
            RenderOrder = renderOrder;
            ViewScope = viewScope;
        }

        protected List<string> Calls => _calls;

        public int RenderOrder { get; }

        public ViewScope ViewScope { get; }

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
            int renderOrder)
            : base(name, calls, renderOrder)
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
            int renderOrder)
            : base(name, calls, renderOrder)
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
