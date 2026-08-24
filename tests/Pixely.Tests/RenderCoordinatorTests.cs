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
        PixelyAppBuilder appBuilder = new();

        Assert.That(appBuilder.IsRegistered<Window>(), Is.False);
    }

    [Test]
    public void UseDefaultRendering_RegistersWindow()
    {
        PixelyAppBuilder appBuilder = new();

        appBuilder.UseDefaultRendering();

        Assert.That(appBuilder.IsRegistered<Window>(), Is.True);
    }

    [Test]
    public void AddWindow_RegistersWindowWithoutRenderCoordinator()
    {
        PixelyAppBuilder appBuilder = new();

        appBuilder.AddWindow();

        Assert.Multiple(() =>
        {
            Assert.That(appBuilder.IsRegistered<Window>(), Is.True);
            Assert.That(appBuilder.IsRegistered<IRenderCoordinator>(), Is.False);
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
        PixelyAppBuilder appBuilder = CreateBuilder(new List<string>());
        ServiceProvider provider = appBuilder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        Assert.DoesNotThrow(renderCoordinator.Execute);
    }

    [Test]
    public void Execute_WhenRenderContextCannotBeCreated_DoesNotRender()
    {
        List<string> calls = new();
        TestRenderContextSource renderContextSource = new() { CanCreate = false };
        PixelyAppBuilder appBuilder = CreateBuilder(calls, renderContextSource);
        appBuilder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls));
        ServiceProvider provider = appBuilder.BuildServiceProvider();
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
        PixelyAppBuilder appBuilder = CreateBuilder(new List<string>(), renderContextSource);
        ServiceProvider provider = appBuilder.BuildServiceProvider();
        IRenderCoordinator renderCoordinator = provider.GetRequiredService<IRenderCoordinator>();

        renderCoordinator.Execute();

        Assert.That(renderContextSource.LastRenderContext?.IsDisposed, Is.True);
    }

    [Test]
    public void Execute_PassesManagedWindowToRenderContextProvider()
    {
        TestRenderContextSource renderContextSource = new();
        PixelyAppBuilder appBuilder = CreateBuilder(new List<string>(), renderContextSource);
        ServiceProvider provider = appBuilder.BuildServiceProvider();

        provider.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.That(renderContextSource.LastWindow, Is.SameAs(provider.GetRequiredService<Window>()));
    }

    [Test]
    public void Execute_RendersOnlyMatchingViewScope()
    {
        ViewScope viewScope = new(7);
        List<string> calls = new();
        PixelyAppBuilder appBuilder = CreateBuilder(calls, viewScope: viewScope);
        appBuilder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("matching", calls, viewScope: viewScope));
        appBuilder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("other", calls));
        ServiceProvider provider = appBuilder.BuildServiceProvider();

        provider.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.That(calls, Is.EqualTo(new[] { "matching" }));
    }

    [Test]
    public void ChildProviderCoordinator_UsesChildWindow()
    {
        PixelyAppBuilder appBuilder = new();
        appBuilder.AddSingleton(new GpuMemorySystem(null!));
        ServiceProvider parent = appBuilder.BuildServiceProvider();
        ServiceCollection childCollection = parent.CreateServiceCollection();
        ViewScope viewScope = new(7);
        Window window = CreateWindow(viewScope, 42);
        TestRenderContextSource renderContextSource = new();
        childCollection.UseWindowRendering<TestRenderContext>(viewScope);
        childCollection.AddSingleton(window);
        childCollection.AddSingleton(renderContextSource);
        childCollection.AddAlias<IRenderContextProvider<TestRenderContext>, TestRenderContextSource>();
        ServiceProvider child = childCollection.BuildServiceProvider();

        child.GetRequiredService<IRenderCoordinator>().Execute();

        Assert.That(renderContextSource.LastWindow, Is.SameAs(window));
    }

    [Test]
    public void ChildProviderRenderer_IsRenderedAfterChildBuild()
    {
        List<string> calls = new();
        PixelyAppBuilder appBuilder = CreateBuilder(calls);
        ServiceProvider parent = appBuilder.BuildServiceProvider();
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
        PixelyAppBuilder appBuilder = CreateBuilder(calls);
        ServiceProvider parent = appBuilder.BuildServiceProvider();
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
        PixelyAppBuilder appBuilder = CreateBuilder(calls);
        appBuilder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls, 10));
        ServiceProvider parent = appBuilder.BuildServiceProvider();
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
        PixelyAppBuilder appBuilder = CreateBuilder(calls);
        appBuilder.AddSingleton<IRenderer<TestRenderContext>>(new TestRenderer("root", calls, 10));
        ServiceProvider parent = appBuilder.BuildServiceProvider();
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
        PixelyAppBuilder appBuilder = CreateBuilder(calls);
        ServiceProvider? parent = null;
        appBuilder.AddSingleton<IRenderer<TestRenderContext>>(new ChildProviderBuildingRenderer("root", calls, () => parent!, 0));
        parent = appBuilder.BuildServiceProvider();
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
        PixelyAppBuilder appBuilder = new();
        appBuilder.UseWindowRendering<TestRenderContext>(viewScope);
        appBuilder.AddSingleton(CreateWindow(viewScope, 42));
        appBuilder.AddSingleton(renderContextSource ?? new TestRenderContextSource());
        appBuilder.AddAlias<IRenderContextProvider<TestRenderContext>, TestRenderContextSource>();
        appBuilder.AddSingleton(new GpuMemorySystem(null!));
        appBuilder.AddSingleton(calls);
        return appBuilder;
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

    private sealed class TestRenderContextSource : IRenderContextProvider<TestRenderContext>
    {
        public bool CanCreate { get; init; } = true;
        public TestRenderContext? LastRenderContext { get; private set; }
        public Window? LastWindow { get; private set; }

        public bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out TestRenderContext? renderContext)
        {
            LastWindow = window;
            if (!CanCreate)
            {
                renderContext = null;
                return false;
            }

            renderContext = new TestRenderContext();
            LastRenderContext = renderContext;
            return true;
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

        public TestRenderer(string name, List<string> calls, int order = 0, ViewScope viewScope = default)
        {
            _name = name;
            _calls = calls;
            Order = order;
            ViewScope = viewScope;
        }

        protected List<string> Calls => _calls;

        public int Order { get; }

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
