using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;

namespace Pixely.Ui.Tests;

/// <summary>
/// The build runs in the update phase. What matters is that it happens at all, that it does not
/// happen for a window nothing will draw, and that each window's system drives its own root against
/// its own colour target.
/// </summary>
public class UiUpdateSystemTests
{
    [Test]
    public void Updating_SetsTheViewportFromTheColourTargetAndBuilds()
    {
        UiRoot root = new();
        root.AddLayer(new Element { Width = Sizing.Fixed(10), Height = Sizing.Fixed(10) });

        UiUpdateSystem<BasicRenderContext> system = System(root, new ShortSize(640, 480));
        system.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(640, 480)));
            Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(640, 480)), "and it built against it");
            Assert.That(root.BuildVersion, Is.EqualTo(1ul));
        });
    }

    [Test]
    public void TheViewportComesFromTheProvider_NotTheWindow()
    {
        UiRoot root = new();
        root.AddLayer(new Element { Width = Sizing.Fixed(10), Height = Sizing.Fixed(10) });

        // The window is uninitialised, so it has no SDL size to give. A viewport taken from the
        // window rather than the provider could not produce this answer.
        UiUpdateSystem<BasicRenderContext> system = System(root, new ShortSize(320, 180));
        system.Update();

        Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(320, 180)));
    }

    [Test]
    public void UpdatingAHiddenWindow_BuildsNothingAndDoesNotEvenAskForTheSize()
    {
        UiRoot root = new();
        SizedContextProvider contextProvider = new(new ShortSize(640, 480));

        UiUpdateSystem<BasicRenderContext> system = new(root, FakeWindow(default, 1, visible: false), contextProvider, 0);
        system.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.BuildVersion, Is.EqualTo(0ul));
            Assert.That(contextProvider.SizeReads, Is.Zero, "a hidden window does not pay for a size call it will not use");
        });
    }

    [TestCase((ushort)0, (ushort)0)]
    [TestCase((ushort)640, (ushort)0)]
    [TestCase((ushort)0, (ushort)480)]
    public void UpdatingAgainstAnEmptyColourTarget_BuildsNothing(ushort width, ushort height)
    {
        UiRoot root = new();
        root.AddLayer(new Element { Width = Sizing.Fixed(10), Height = Sizing.Fixed(10) });

        UiUpdateSystem<BasicRenderContext> system = System(root, new ShortSize(width, height));
        system.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.BuildVersion, Is.EqualTo(0ul));
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(0, 0)), "and the viewport is left alone rather than invalidating every layer");
        });
    }

    [Test]
    public void TheSystemsRunInOrderAndJoinTheUpdatablesByBeingRegistered()
    {
        List<string> calls = new();

        // Registered late first, so passing cannot be an accident of registration order, which is
        // not a guarantee anyway: the registry sorts with an unstable sort.
        PixelyAppBuilder builder = new();
        builder.AddSingleton<UiUpdateSystem<BasicRenderContext>>(_ => Recording(calls, "late", 10));
        builder.AddSingleton<UiUpdateSystem<BasicRenderContext>>(_ => Recording(calls, "early", -10));

        ServiceProvider provider = builder.BuildServiceProvider();

        foreach (IUpdatable updatable in provider.GetRequiredService<ServiceRegistry<IUpdatable>>())
        {
            updatable.Update();
        }

        Assert.That(calls, Is.EqualTo(new[] { "early", "late" }));
    }

    [Test]
    public void EachViewScope_ResolvesItsOwnRootAndWindow()
    {
        ViewScope first = new(1);
        ViewScope second = new(2);
        UiRoot firstRoot = new();
        UiRoot secondRoot = new();
        Window firstWindow = FakeWindow(first, 1);
        Window secondWindow = FakeWindow(second, 2);

        ServiceProvider provider = Resolvable(
            new SizedContextProvider(new ShortSize(320, 240)),
            (first, firstRoot, firstWindow),
            (second, secondRoot, secondWindow));

        (UiRoot resolvedFirstRoot, Window resolvedFirstWindow, _) = UiExtensions.ResolveUpdateTargets<BasicRenderContext>(provider, first);
        (UiRoot resolvedSecondRoot, Window resolvedSecondWindow, _) = UiExtensions.ResolveUpdateTargets<BasicRenderContext>(provider, second);

        Assert.Multiple(() =>
        {
            Assert.That(resolvedFirstRoot, Is.SameAs(firstRoot));
            Assert.That(resolvedFirstWindow, Is.SameAs(firstWindow));
            Assert.That(resolvedSecondRoot, Is.SameAs(secondRoot));
            Assert.That(resolvedSecondWindow, Is.SameAs(secondWindow));
        });
    }

    [Test]
    public void TheResolvedProvider_IsTheOneRegistered()
    {
        ViewScope viewScope = new(1);
        SizedContextProvider contextProvider = new(new ShortSize(640, 360));

        ServiceProvider provider = Resolvable(contextProvider, (viewScope, new UiRoot(), FakeWindow(viewScope, 1)));

        (UiRoot _, Window _, RenderContextProvider<BasicRenderContext> resolved) =
            UiExtensions.ResolveUpdateTargets<BasicRenderContext>(provider, viewScope);

        Assert.That(resolved, Is.SameAs(contextProvider));
    }

    private static UiUpdateSystem<BasicRenderContext> System(UiRoot root, ShortSize colorTargetSize, int updateOrder = 0)
    {
        return new UiUpdateSystem<BasicRenderContext>(root, FakeWindow(default, 1), new SizedContextProvider(colorTargetSize), updateOrder);
    }

    /// <summary>
    /// A system whose provider records when it was asked for a size, which is what makes the order
    /// two systems ran in observable.
    /// </summary>
    private static UiUpdateSystem<BasicRenderContext> Recording(List<string> calls, string name, int updateOrder)
    {
        SizedContextProvider contextProvider = new(new ShortSize(320, 240), () => calls.Add(name));
        return new UiUpdateSystem<BasicRenderContext>(new UiRoot(), FakeWindow(default, 1), contextProvider, updateOrder);
    }

    private static ServiceProvider Resolvable(
        RenderContextProvider<BasicRenderContext> contextProvider,
        params (ViewScope ViewScope, UiRoot Root, Window Window)[] scopes)
    {
        PixelyAppBuilder builder = new();
        builder.AddRegistry<ScopedUiRoot>();
        builder.AddSingleton<RenderContextProvider<BasicRenderContext>>(_ => contextProvider);

        foreach ((ViewScope viewScope, UiRoot root, Window window) in scopes)
        {
            builder.AddSingleton(window);
            builder.AddSingleton<ScopedUiRoot>(_ => new ScopedUiRoot(viewScope, root));
        }

        return builder.BuildServiceProvider();
    }

    /// <summary>A provider that answers with a fixed colour target size and never creates a context.</summary>
    private sealed class SizedContextProvider : RenderContextProvider<BasicRenderContext>
    {
        private readonly ShortSize _size;
        private readonly Action? _onSizeRead;

        internal SizedContextProvider(ShortSize size, Action? onSizeRead = null)
        {
            _size = size;
            _onSizeRead = onSizeRead;
        }

        internal int SizeReads { get; private set; }

        public override ShortSize GetColorTargetSize(Window window)
        {
            SizeReads++;
            _onSizeRead?.Invoke();
            return _size;
        }

        public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
        {
            renderContext = null;
            return false;
        }
    }

    /// <summary>
    /// A window that never reaches SDL. Constructed uninitialised, so only what is set here answers;
    /// <see cref="Window.IsVisible"/> is overridden because SDL is otherwise its only source.
    /// </summary>
    private sealed class TestWindow : Window
    {
        // Never runs. The instance is created uninitialised, because the real constructor ends by
        // reading RenderSizeInPixels from SDL. This exists only because a derived class must name a
        // base constructor to compile.
        private TestWindow()
            : base(default, default, default, 0, null!, null!, default)
        {
        }

        internal bool Visible { get; set; }

        public override bool IsVisible => Visible;
    }

    private static Window FakeWindow(ViewScope viewScope, uint sdlId, bool visible = true)
    {
        TestWindow window = (TestWindow)RuntimeHelpers.GetUninitializedObject(typeof(TestWindow));
        window.Visible = visible;
        SetBackingField(window, nameof(Window.ViewScope), viewScope);

        // The window registry keys on this, and two windows sharing an id is what it refuses.
        SetBackingField(window, nameof(Window.SdlId), sdlId);
        return window;
    }

    private static void SetBackingField<T>(Window window, string propertyName, T value)
    {
        FieldInfo field = typeof(Window).GetField($"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, value);
    }
}
