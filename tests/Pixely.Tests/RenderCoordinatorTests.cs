using System.Diagnostics.CodeAnalysis;
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
            Assert.That(renderContextSource.CreatedCount, Is.Zero);
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

        Assert.That(renderContextSource.DisposedCount, Is.EqualTo(1));
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
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
        SetBackingField(window, nameof(Window.ViewScope), viewScope);
        SetBackingField(window, nameof(Window.SdlId), sdlId);
        return window;
    }

    private static void SetBackingField<T>(Window window, string propertyName, T value)
    {
        FieldInfo field = typeof(Window).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, value);
    }

    private sealed class TestRenderContextSource : RenderContextProvider<TestRenderContext>
    {
        public bool CanCreate { get; init; } = true;
        public int CreatedCount { get; private set; }
        public int DisposedCount { get; set; }
        public Window? LastWindow { get; private set; }

        public override bool TryCreateRenderContext(Window window, [MaybeNullWhen(false)] out TestRenderContext renderContext)
        {
            LastWindow = window;
            if (!CanCreate)
            {
                renderContext = default;
                return false;
            }

            renderContext = new TestRenderContext(this);
            CreatedCount++;
            return true;
        }
    }

    private ref struct TestRenderContext : IRenderContext
    {
        private readonly TestRenderContextSource _source;
        private CommandBuffer _commandBuffer;

        public TestRenderContext(TestRenderContextSource source)
        {
            _source = source;
        }

        [UnscopedRef]
        public ref CommandBuffer CommandBuffer => ref _commandBuffer;

        public readonly Texture ColorTarget => null!;

        public readonly void Dispose()
        {
            _source.DisposedCount++;
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

        public virtual void Render(ref TestRenderContext renderContext)
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

        public override void Render(ref TestRenderContext renderContext)
        {
            base.Render(ref renderContext);
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

        public override void Render(ref TestRenderContext renderContext)
        {
            base.Render(ref renderContext);

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
