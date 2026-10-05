using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Utilities;
using SDL;

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
    public void Render_WithNoRenderers_DoesNotThrow()
    {
        PixelyAppBuilder builder = CreateBuilder(new List<string>());
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        Assert.DoesNotThrow(() => Render(renderCoordinator));
    }

    [Test]
    public void Render_DisposesRenderContext()
    {
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder builder = CreateBuilder(new List<string>(), renderContextSource);
        ServiceProvider provider = builder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        Render(renderCoordinator);

        Assert.That(renderContextSource.LastRenderContext?.IsDisposed, Is.True);
    }

    [Test]
    public void Render_PassesManagedWindowToRenderContextProvider()
    {
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder builder = CreateBuilder(new List<string>(), renderContextSource);
        ServiceProvider provider = builder.BuildServiceProvider();

        Render(provider.GetRequiredService<IRenderCoordinator>());

        Assert.That(renderContextSource.LastWindow, Is.SameAs(provider.GetRequiredService<Window>()));
    }

    [Test]
    public void Render_RendersOnlyMatchingViewScope()
    {
        ViewScope viewScope = new(7);
        List<string> calls = new();
        PixelyAppBuilder builder = CreateBuilder(calls, viewScope: viewScope);
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("matching", calls, viewScope: viewScope));
        builder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("other", calls));
        ServiceProvider provider = builder.BuildServiceProvider();

        Render(provider.GetRequiredService<IRenderCoordinator>());

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

        Render(child.GetRequiredService<IRenderCoordinator>());

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

        Render(renderCoordinator);

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
        Render(renderCoordinator);

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

        Render(renderCoordinator);

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

        Render(renderCoordinator);

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

        Render(renderCoordinator);

        Assert.That(calls, Is.EqualTo(new[] { "root" }));

        calls.Clear();
        Render(renderCoordinator);

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

    // Runs the part of a frame after the GPU acquire, which needs no GPU device. The test contexts never use the command buffer
    // or the texture.
    private static void Render(IRenderCoordinator renderCoordinator)
    {
        ((RenderCoordinator<TestRenderContext>)renderCoordinator).Render(null!, null!);
    }

    [Test]
    public void Execute_WhenNoTextureComesBack_SubmitsUploadsAndFrameWithoutCallingProvider()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new();
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(calls, renderContextSource, hasTexture: false);

        bool drawn = coordinator.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.False);
            Assert.That(renderContextSource.LastWindow, Is.Null);
            Assert.That(calls, Is.EqualTo(new[] { "acquire", "uploads", "submit" }));
        });
    }

    [Test]
    public void Execute_WhenWindowIsNotRenderable_SubmitsFrameWithoutContext()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new();
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(calls, renderContextSource, renderable: false);

        bool drawn = coordinator.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.False);
            Assert.That(renderContextSource.LastWindow, Is.Null);
            Assert.That(calls, Is.EqualTo(new[] { "acquire", "uploads", "submit" }));
        });
    }

    [Test]
    public void Execute_WhenDrawn_SubmitsUploadsBeforeFrameAfterContextIsDisposed()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new(calls);
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(calls, renderContextSource);

        bool drawn = coordinator.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(drawn, Is.True);
            Assert.That(calls, Is.EqualTo(new[] { "acquire", "root", "dispose", "uploads", "submit" }));
        });
    }

    [Test]
    public void Execute_WhenRendererThrows_StillSubmitsUploadsBeforeFrame()
    {
        List<string> calls = new();
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(calls, new TestRenderContextSource(calls), new ThrowingRenderer());

        Assert.Throws<InvalidOperationException>(() => coordinator.Execute());
        Assert.That(calls, Is.EqualTo(new[] { "acquire", "dispose", "uploads", "submit" }));
    }

    [Test]
    public void Execute_WithDiagnostics_CountsOnlyTheAcquireAsSwapchainWait()
    {
        RecordingLogger logger = new();
        long now = 0;
        // One timestamp tick is one second, so the five-tick frame ends a report period.
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, 1);
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(new List<string>(), new TestRenderContextSource(),
            new ActionRenderer(() => now += 1), diagnostics: diagnostics, acquiring: () => now += 4);

        diagnostics.BeginFrame();
        diagnostics.EndUpdate();
        coordinator.Execute();
        diagnostics.EndRender();
        diagnostics.EndFrame();

        Assert.Multiple(() =>
        {
            Assert.That(logger.Messages.First(), Is.EqualTo("Swapchain of view 0: 640x480, B8G8R8A8Unorm, present mode vsync"));
            Assert.That(logger.Messages.Last(), Does.Contain(", render 1000.00/1000.00/1000.00, swapchain wait 4000.00/4000.00/4000.00;"));
        });
    }

    [Test]
    public void Execute_WithDiagnosticsWhenNoTextureComesBack_ReportsNoSwapchain()
    {
        RecordingLogger logger = new();
        PerformanceDiagnostics diagnostics = new(logger, null, () => 0, 1);
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(new List<string>(), new TestRenderContextSource(), hasTexture: false,
            diagnostics: diagnostics);

        coordinator.Execute();

        Assert.That(logger.Messages, Is.Empty);
    }

    [Test]
    public void UseWindowRendering_PassesRegisteredDiagnosticsToTheCoordinator()
    {
        PerformanceDiagnostics diagnostics = new(null, null);
        PixelyAppBuilder builder = CreateBuilder(new List<string>());
        builder.AddSingleton(diagnostics);
        ServiceProvider provider = builder.BuildServiceProvider();

        IRenderCoordinator coordinator = provider.GetRequiredService<IRenderCoordinator>();

        FieldInfo field = coordinator.GetType().GetField("_diagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(field.GetValue(coordinator), Is.SameAs(diagnostics));
    }

    private static RenderCoordinator<TestRenderContext> CreateCoordinator(
        List<string> calls,
        TestRenderContextSource renderContextSource,
        IRenderer<TestRenderContext>? renderer = null,
        bool renderable = true,
        bool hasTexture = true,
        PerformanceDiagnostics? diagnostics = null,
        Action? acquiring = null)
    {
        PixelyAppBuilder builder = CreateBuilder(calls, renderContextSource);
        builder.AddSingleton(renderer ?? new TestRenderer("root", calls));
        ServiceProvider provider = builder.BuildServiceProvider();
        TestWindow window = (TestWindow)provider.GetRequiredService<Window>();
        window.Renderable = renderable;
        window.HasTexture = hasTexture;
        window.Acquiring = acquiring;
        return new RenderCoordinator<TestRenderContext>(
            window,
            new RecordingGpu(calls),
            renderContextSource,
            provider.GetRequiredService<ServiceRegistry<IRenderer<TestRenderContext>>>(),
            diagnostics);
    }

    // Records the coordinator's GPU calls in order. The command buffer is a token the test contexts never use.
    private sealed class RecordingGpu : IRenderCoordinatorGpu
    {
        private readonly List<string> _calls;

        public RecordingGpu(List<string> calls)
        {
            _calls = calls;
        }

        public CommandBuffer AcquireCommandBuffer()
        {
            _calls.Add("acquire");
            return (CommandBuffer)RuntimeHelpers.GetUninitializedObject(typeof(CommandBuffer));
        }

        public void SubmitUploads()
        {
            _calls.Add("uploads");
        }

        public void Submit(CommandBuffer commandBuffer)
        {
            _calls.Add("submit");
        }

        public void Cancel(CommandBuffer commandBuffer)
        {
            _calls.Add("cancel");
        }
    }

    private sealed class ActionRenderer(Action action) : IRenderer<TestRenderContext>
    {
        public int RenderOrder => 0;

        public void Render(TestRenderContext renderContext)
        {
            action();
        }
    }

    private sealed class ThrowingRenderer : IRenderer<TestRenderContext>
    {
        public int RenderOrder => 0;

        public void Render(TestRenderContext renderContext)
        {
            throw new InvalidOperationException("renderer failed");
        }
    }

    private static PixelyAppBuilder CreateBuilder(
        List<string> calls,
        TestRenderContextSource? renderContextSource = null,
        ViewScope viewScope = default)
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(CreateGpuDeviceStub());
        builder.UseWindowRendering<TestRenderContext>(viewScope);
        builder.AddSingleton(CreateWindow(viewScope, 42));
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

    private static Window CreateWindow(ViewScope viewScope, uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(TestWindow));
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
    // constructor to compile.
    private sealed class TestWindow : Window
    {
        private TestWindow()
            : base(default, default, 0, null!, null!, default)
        {
        }

        // Set by the test: the window is created uninitialised, so initialisers would not run.
        public bool Renderable { get; set; }

        public bool HasTexture { get; set; }

        // Runs while the texture is acquired, standing in for the wait.
        public Action? Acquiring { get; set; }

        public override bool IsRenderable => Renderable;

        public override TextureFormat ColorTargetFormat => throw new NotSupportedException();

        public override bool TryWaitAndAcquireSwapchainTexture(CommandBuffer commandBuffer, out SwapchainTexture swapchainTexture)
        {
            Acquiring?.Invoke();
            swapchainTexture = new SwapchainTexture(Pointer<SDL_GPUTexture>.Null, new ShortSize(640, 480), TextureFormat.B8G8R8A8Unorm);
            return HasTexture;
        }
    }

    private sealed class TestRenderContextSource : RenderContextProvider<TestRenderContext>
    {
        private readonly List<string>? _calls;

        public TestRenderContextSource(List<string>? calls = null)
        {
            _calls = calls;
        }

        public TestRenderContext? LastRenderContext { get; private set; }
        public Window? LastWindow { get; private set; }

        public override TestRenderContext CreateRenderContext(FrameContext frameContext)
        {
            LastWindow = frameContext.Window;
            LastRenderContext = new TestRenderContext(_calls);
            return LastRenderContext;
        }
    }

    private sealed class TestRenderContext : IRenderContext
    {
        public CommandBuffer CommandBuffer => null!;

        public Texture ColorTarget => null!;

        private readonly List<string>? _calls;

        public TestRenderContext(List<string>? calls = null)
        {
            _calls = calls;
        }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
            _calls?.Add("dispose");
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
