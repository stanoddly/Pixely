using System.Reflection;
using System.Runtime.CompilerServices;

namespace Pixely.Tests;

public sealed class WindowRegistryTests
{
    [Test]
    public void GetWindow_WithoutScope_ReturnsDefaultWindow()
    {
        WindowRegistry windowRegistry = new();
        Window window = CreateWindow(default, 42);

        windowRegistry.Register(window);

        Assert.That(windowRegistry.GetWindow(), Is.SameAs(window));
    }

    [Test]
    public void Register_IndexesWindowByViewScopeAndSdlId()
    {
        WindowRegistry windowRegistry = new();
        Window window = CreateWindow(new ViewScope(7), 42);

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
        windowRegistry.Register(CreateWindow(new ViewScope(7), 42));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
            windowRegistry.Register(CreateWindow(new ViewScope(7), 43)));

        Assert.That(exception!.Message, Does.Contain("ViewScope 7"));
    }

    [Test]
    public void Register_DuplicateSdlId_Throws()
    {
        WindowRegistry windowRegistry = new();
        windowRegistry.Register(CreateWindow(new ViewScope(7), 42));

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
            windowRegistry.Register(CreateWindow(new ViewScope(8), 42)));

        Assert.That(exception!.Message, Does.Contain("SDL window ID 42"));
    }

    [Test]
    public void Register_NegativeViewScope_IndexesWindow()
    {
        WindowRegistry windowRegistry = new();
        Window window = CreateWindow(new ViewScope(-1), 42);

        windowRegistry.Register(window);

        Assert.That(windowRegistry.GetWindow(new ViewScope(-1)), Is.SameAs(window));
    }

    [Test]
    public void Unregister_RemovesOnlyMatchingWindow()
    {
        WindowRegistry windowRegistry = new();
        Window first = CreateWindow(new ViewScope(7), 42);
        Window second = CreateWindow(new ViewScope(9), 43);
        windowRegistry.Register(first);
        windowRegistry.Register(second);

        windowRegistry.Unregister(first);

        Assert.Multiple(() =>
        {
            Assert.That(windowRegistry.TryGetWindow(new ViewScope(7), out Window _), Is.False);
            Assert.That(windowRegistry.GetWindow(new ViewScope(9)), Is.SameAs(second));
        });
    }

    private static Window CreateWindow(ViewScope viewScope, uint sdlId)
    {
        Window window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(SwapchainWindow));
        SetBackingField(window, nameof(Window.ViewScope), viewScope);
        SetBackingField(window, nameof(Window.SdlId), sdlId);
        return window;
    }

    private static void SetBackingField<T>(Window window, string propertyName, T value)
    {
        FieldInfo field = typeof(Window).GetField(
            $"<{propertyName}>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(window, value);
    }
}
