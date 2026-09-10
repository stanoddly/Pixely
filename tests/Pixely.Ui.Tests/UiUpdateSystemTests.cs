using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Reflection;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.RenderOrchestration;

namespace Pixely.Ui.Tests;

/// <summary>
/// The build runs in the update phase. What matters is that it happens at all, that it does not
/// happen for a window nothing will draw, and that each window's system drives its own root.
/// </summary>
public class UiUpdateSystemTests
{
    [Test]
    public void Updating_SetsTheViewportFromItsSourceAndBuilds()
    {
        UiRoot root = new();
        root.AddLayer(new Element { Width = Sizing.Fixed(10), Height = Sizing.Fixed(10) });

        UiUpdateSystem system = new(root, () => new Vector2Int(640, 480), () => true, 0);
        system.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(640, 480)));
            Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(640, 480)), "and it built against it");
            Assert.That(root.BuildVersion, Is.EqualTo(1ul));
        });
    }

    [Test]
    public void UpdatingAHiddenWindow_BuildsNothingAndDoesNotEvenAskForTheSize()
    {
        UiRoot root = new();
        bool sizeRead = false;

        UiUpdateSystem system = new(
            root,
            () =>
            {
                sizeRead = true;
                return new Vector2Int(640, 480);
            },
            () => false,
            0);

        system.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.BuildVersion, Is.EqualTo(0ul));
            Assert.That(sizeRead, Is.False, "a hidden window does not pay for a size call it will not use");
        });
    }

    [TestCase(0, 0)]
    [TestCase(640, 0)]
    [TestCase(0, 480)]
    [TestCase(-1, 480)]
    public void UpdatingAgainstAnEmptyViewport_BuildsNothing(int width, int height)
    {
        UiRoot root = new();
        root.AddLayer(new Element { Width = Sizing.Fixed(10), Height = Sizing.Fixed(10) });

        UiUpdateSystem system = new(root, () => new Vector2Int(width, height), () => true, 0);
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

        // Registered late first, so passing cannot be an accident of registration order — which is
        // not a guarantee anyway, since the registry sorts with an unstable sort.
        PixelyAppBuilder builder = new();
        builder.AddSingleton<UiUpdateSystem>(_ => new UiUpdateSystem(new UiRoot(), () => Recording(calls, "late"), () => RecordingVisible(calls, "late visible"), 10));
        builder.AddSingleton<UiUpdateSystem>(_ => new UiUpdateSystem(new UiRoot(), () => Recording(calls, "early"), () => RecordingVisible(calls, "early visible"), -10));

        ServiceProvider provider = builder.BuildServiceProvider();

        foreach (IUpdatable updatable in provider.GetRequiredService<ServiceRegistry<IUpdatable>>())
        {
            updatable.Update();
        }

        Assert.That(calls, Is.EqualTo(new[] { "early visible", "early", "late visible", "late" }));
    }

    [Test]
    public void EachViewScope_ResolvesItsOwnRootAndWindow()
    {
        ViewScope first = new(1);
        ViewScope second = new(2);
        UiRoot firstRoot = new();
        UiRoot secondRoot = new();
        Window firstWindow = UninitialisedWindow(first, 1);
        Window secondWindow = UninitialisedWindow(second, 2);

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
    public void TheViewportComesFromTheContextProvider_NotTheWindow()
    {
        ViewScope viewScope = new(1);
        UiRoot root = new();
        SizedContextProvider contextProvider = new(new ShortSize(640, 360));

        ServiceProvider provider = Resolvable(contextProvider, (viewScope, root, UninitialisedWindow(viewScope, 1)));

        (UiRoot _, Window window, RenderContextProvider<BasicRenderContext> resolved) =
            UiExtensions.ResolveUpdateTargets<BasicRenderContext>(provider, viewScope);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(contextProvider));

            // Reading it through the resolved provider is the whole point: an uninitialised window
            // has no SDL size to give, so a viewport that came from the window could not answer.
            Assert.That(resolved.GetColorTargetSize(window), Is.EqualTo(new ShortSize(640, 360)));
        });
    }

    /// <summary>A provider that answers with a fixed colour target size and never creates a context.</summary>
    private sealed class SizedContextProvider : RenderContextProvider<BasicRenderContext>
    {
        private readonly ShortSize _size;

        internal SizedContextProvider(ShortSize size)
        {
            _size = size;
        }

        public override ShortSize GetColorTargetSize(Window window)
        {
            return _size;
        }

        public override bool TryCreateRenderContext(Window window, [NotNullWhen(true)] out BasicRenderContext? renderContext)
        {
            renderContext = null;
            return false;
        }
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

    /// <summary>
    /// Records that a delegate was reached, and answers so that nothing is built: a zero viewport
    /// for the size, and visible for the visibility, so both delegates of both systems are reached.
    /// </summary>
    private static Vector2Int Recording(List<string> calls, string name)
    {
        calls.Add(name);
        return new Vector2Int(0, 0);
    }

    /// <inheritdoc cref="Recording(List{string}, string)"/>
    private static bool RecordingVisible(List<string> calls, string name)
    {
        calls.Add(name);
        return true;
    }

    /// <summary>
    /// A window that never reaches SDL. Every property this test touches is set here, which is what
    /// keeps the scope lookup testable without a display.
    /// </summary>
    private static Window UninitialisedWindow(ViewScope viewScope, uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
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
