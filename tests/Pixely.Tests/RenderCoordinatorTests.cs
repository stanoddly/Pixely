using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Testing;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public class RenderCoordinatorTests
{
    [Test]
    public void UseDefaultRendering_RegistersTheCoordinatorAndBasicRenderContextProviderButNoWindow()
    {
        ServiceCollection services = new();

        services.UseDefaultRendering();

        Assert.Multiple(() =>
        {
            Assert.That(services.IsRegistered<IRenderCoordinator>(), Is.True);
            Assert.That(services.IsRegistered<RenderContextProvider<BasicRenderContext>>(), Is.True);
            Assert.That(services.IsRegistered<Window>(), Is.False);
        });
    }

    [Test]
    public void UseWindowRenderingOfBasicRenderContext_RegistersBasicRenderContextProvider()
    {
        ServiceCollection services = new();

        services.UseWindowRendering<BasicRenderContext>();

        Assert.That(services.IsRegistered<RenderContextProvider<BasicRenderContext>>(), Is.True);
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
    public void StageCoordinator_UsesTheRootWindow()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(CreateGpuDeviceStub());
        builder.AddSingleton(new GpuMemorySystem(null!));
        Window window = CreateWindow(42);
        builder.AddSingleton(window);
        ServiceProvider parent = builder.BuildServiceProvider();
        ServiceCollection childCollection = parent.CreateServiceCollection();
        TestRenderContextSource renderContextSource = new();
        childCollection.UseWindowRendering<TestRenderContext>();
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
    public void GetRenderCoordinator_WithoutCoordinators_Throws()
    {
        PixelyInitializationException? exception = Assert.Throws<PixelyInitializationException>(() => PixelyApp.GetRenderCoordinator(CreateCoordinatorRegistry()));

        Assert.That(exception!.Message, Does.Contain("no render coordinator"));
    }

    [Test]
    public void GetRenderCoordinator_WithTwoCoordinators_Throws()
    {
        PixelyInitializationException? exception = Assert.Throws<PixelyInitializationException>(() => PixelyApp.GetRenderCoordinator(CreateCoordinatorRegistry(false, true)));

        Assert.That(exception!.Message, Does.Contain("more than one render coordinator"));
    }

    [Test]
    public void GetRenderCoordinator_WithOneCoordinator_ReturnsIt()
    {
        ServiceRegistry<IRenderCoordinator> registry = CreateCoordinatorRegistry(true);

        Assert.That(PixelyApp.GetRenderCoordinator(registry), Is.SameAs(registry.Single()));
    }

    [Test]
    public void GetRenderCoordinator_AfterAStageWithACoordinatorReplacesAnother_ReturnsTheNewStagesCoordinator()
    {
        ServiceProvider root = new PixelyAppBuilder().BuildServiceProvider();
        ServiceRegistry<IRenderCoordinator> registry = root.GetRequiredService<ServiceRegistry<IRenderCoordinator>>();
        StubRenderCoordinator first = new(true);
        StubRenderCoordinator second = new(true);
        ServiceCollection firstStage = root.CreateServiceCollection();
        firstStage.AddSingleton<IRenderCoordinator>(first);
        ServiceProvider firstProvider = firstStage.BuildServiceProvider();
        IRenderCoordinator beforeSwitch = PixelyApp.GetRenderCoordinator(registry);

        firstProvider.Dispose();
        ServiceCollection secondStage = root.CreateServiceCollection();
        secondStage.AddSingleton<IRenderCoordinator>(second);
        using ServiceProvider secondProvider = secondStage.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(beforeSwitch, Is.SameAs(first));
            Assert.That(PixelyApp.GetRenderCoordinator(registry), Is.SameAs(second));
        });
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
    public void Execute_WithFrameTimings_CountsOnlyTheAcquireAsSwapchainWait()
    {
        long now = 0;
        FrameTimings timings = new(() => now, 1);
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(new List<string>(), new TestRenderContextSource(),
            new ActionRenderer(() => now += 1), frameTimingRecorder: timings, acquiring: () => now += 4);

        timings.BeginFrame();
        timings.EndUpdate();
        coordinator.Execute();
        timings.EndRender();
        timings.EndFrame();

        Assert.Multiple(() =>
        {
            Assert.That(timings.SwapchainWaitTimes.ToArray(), Is.EqualTo(new[] { 4.0 }));
            Assert.That(timings.RenderTimes.ToArray(), Is.EqualTo(new[] { 1.0 }));
            Assert.That(timings.SwapchainChanges, Is.EqualTo(new[] { new SwapchainState(new ShortSize(640, 480), TextureFormat.B8G8R8A8Unorm, false) }));
        });
    }

    [Test]
    public void Execute_WithFrameTimingsWhenNoTextureComesBack_RecordsNoSwapchain()
    {
        FrameTimings timings = new(() => 0, 1);
        RenderCoordinator<TestRenderContext> coordinator = CreateCoordinator(new List<string>(), new TestRenderContextSource(), hasTexture: false,
            frameTimingRecorder: timings);

        coordinator.Execute();

        Assert.That(timings.SwapchainChanges, Is.Empty);
    }

    [Test]
    public void UseWindowRendering_PassesTheRegisteredFrameTimingRecorderToTheCoordinator()
    {
        FrameTimings timings = new();
        PixelyAppBuilder builder = CreateBuilder(new List<string>());
        builder.AddSingleton<IFrameTimingRecorder>(timings);
        ServiceProvider provider = builder.BuildServiceProvider();

        IRenderCoordinator coordinator = provider.GetRequiredService<IRenderCoordinator>();

        FieldInfo field = coordinator.GetType().GetField("_frameTimingRecorder", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(field.GetValue(coordinator), Is.SameAs(timings));
    }

    private static RenderCoordinator<TestRenderContext> CreateCoordinator(
        List<string> calls,
        TestRenderContextSource renderContextSource,
        IRenderer<TestRenderContext>? renderer = null,
        bool renderable = true,
        bool hasTexture = true,
        IFrameTimingRecorder? frameTimingRecorder = null,
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
            frameTimingRecorder ?? NullFrameTimingRecorder.Instance);
    }

    // Records the coordinator's GPU calls in order. The command buffer is a token the test contexts never use.
    private sealed class RecordingGpu : IRenderCoordinatorGpu
    {
        private readonly StubGpuDevice _gpuDevice = StubGpuDevice.Create();
        private readonly List<string> _calls;

        public RecordingGpu(List<string> calls)
        {
            _calls = calls;
        }

        public CommandBuffer AcquireCommandBuffer()
        {
            _calls.Add("acquire");
            return _gpuDevice.AcquireCommandBuffer();
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

    private static PixelyAppBuilder CreateBuilder(List<string> calls, TestRenderContextSource? renderContextSource = null)
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(CreateGpuDeviceStub());
        builder.UseWindowRendering<TestRenderContext>();
        builder.AddSingleton(CreateWindow(42));
        builder.AddSingleton(renderContextSource ?? new TestRenderContextSource());
        builder.AddAlias<RenderContextProvider<TestRenderContext>, TestRenderContextSource>();
        builder.AddSingleton(new GpuMemorySystem(null!));
        builder.AddSingleton(calls);
        return builder;
    }

    // The builder's own Build() is never called here, so the stub stands in for the device it would register.
    private static GpuDevice CreateGpuDeviceStub()
    {
        return StubGpuDevice.Create();
    }

    private static Window CreateWindow(uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(TestWindow));
        FieldInfo field = typeof(Window).GetField($"<{nameof(Window.SdlId)}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, sdlId);
        return window;
    }

    // Created uninitialised, so it never reaches SDL. The constructor exists only because a derived class must name a base
    // constructor to compile.
    private sealed class TestWindow : Window
    {
        private TestWindow()
            : base(default, 0, null!, null!)
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

        public TestRenderer(string name, List<string> calls, int renderOrder = 0)
        {
            _name = name;
            _calls = calls;
            RenderOrder = renderOrder;
        }

        protected List<string> Calls => _calls;

        public int RenderOrder { get; }

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
