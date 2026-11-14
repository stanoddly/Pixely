using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pixely.Tests;

public sealed class WindowRegistryTests
{
    [Test]
    public void GetWindow_WithoutScope_ReturnsDefaultWindow()
    {
        WindowRegistry windowRegistry = new();
        Window window = new FixedWindow(default);

        windowRegistry.Register(window);

        Assert.That(windowRegistry.GetWindow(), Is.SameAs(window));
    }

    [Test]
    public void Register_IndexesWindowByViewScopeAndSdlId()
    {
        WindowRegistry windowRegistry = new();
        Window window = CreateNativeWindow(new ViewScope(7), 42);

        windowRegistry.Register(window);

        Assert.Multiple(() =>
        {
            Assert.That(windowRegistry.GetWindow(new ViewScope(7)), Is.SameAs(window));
            Assert.That(windowRegistry.TryGetWindow(42, out Window bySdlId), Is.True);
            Assert.That(bySdlId, Is.SameAs(window));
        });
    }

    [Test]
    public void Register_DuplicateViewScope_Throws()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(new FixedWindow(new ViewScope(7)));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
            windowRegistry.Register(new FixedWindow(new ViewScope(7))));

        Assert.That(exception!.Message, Does.Contain("ViewScope 7"));
    }

    [Test]
    public void Register_DuplicateSdlId_Throws()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(CreateNativeWindow(new ViewScope(7), 42));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
            windowRegistry.Register(CreateNativeWindow(new ViewScope(8), 42)));

        Assert.That(exception!.Message, Does.Contain("SDL window ID 42"));
    }

    [Test]
    public void Register_NegativeViewScope_IndexesWindow()
    {
        WindowRegistry windowRegistry = new();
        Window window = new FixedWindow(new ViewScope(-1));

        windowRegistry.Register(window);

        Assert.That(windowRegistry.GetWindow(new ViewScope(-1)), Is.SameAs(window));
    }

    [Test]
    public void Unregister_RemovesOnlyMatchingWindow()
    {
        WindowRegistry windowRegistry = new();
        Window first = new FixedWindow(new ViewScope(7));
        Window second = new FixedWindow(new ViewScope(9));
        windowRegistry.Register(first);
        windowRegistry.Register(second);

        windowRegistry.Unregister(first);

        Assert.Multiple(() =>
        {
            Assert.That(windowRegistry.TryGetWindow(new ViewScope(7), out Window _), Is.False);
            Assert.That(windowRegistry.GetWindow(new ViewScope(9)), Is.SameAs(second));
        });
    }

    [Test]
    public void Register_ANativeAndAWindowWithoutAPlatform_TakesBoth()
    {
        WindowRegistry windowRegistry = new();
        Window native = CreateNativeWindow(new ViewScope(7), 42);
        Window withoutPlatform = new FixedWindow(new ViewScope(8));

        windowRegistry.Register(native);
        windowRegistry.Register(withoutPlatform);

        Assert.Multiple(() =>
        {
            Assert.That(windowRegistry.GetWindow(new ViewScope(8)), Is.SameAs(withoutPlatform));
            Assert.That(windowRegistry.TryGetWindow(42, out Window bySdlId), Is.True);
            Assert.That(bySdlId, Is.SameAs(native));
        });
    }

    // Created uninitialised, so it never reaches SDL.
    private static Window CreateNativeWindow(ViewScope viewScope, uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(SwapchainWindow));
        SetBackingField<Window, ViewScope>(window, nameof(Window.ViewScope), viewScope);
        SetBackingField<NativeWindow, uint>(window, nameof(NativeWindow.SdlId), sdlId);
        return window;
    }

    // The property's backing field is declared on TDeclaringWindow.
    private static void SetBackingField<TDeclaringWindow, T>(Window window, string propertyName, T value) where TDeclaringWindow : Window
    {
        FieldInfo field = typeof(TDeclaringWindow).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, value);
    }
}
