using System.Reflection;
using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.DependencyInjection;

namespace Pixely.Tests;

public sealed class WindowServiceProviderExtensionsTests
{
    [Test]
    public void GetWindow_ActivatesWindowRegisteredAfterConsumer()
    {
        ViewScope viewScope = new(7);
        Window window = CreateWindow(viewScope, 42);
        ServiceCollection services = new();
        services.AddSingleton<WindowConsumer>(provider => new WindowConsumer(provider.GetWindow(viewScope)));
        services.AddSingleton(window);

        ServiceProvider provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<WindowConsumer>().Window, Is.SameAs(window));
    }

    [Test]
    public void GetWindow_FromChildProvider_ActivatesChildWindowRegisteredAfterConsumer()
    {
        ViewScope parentScope = new(7);
        ViewScope childScope = new(9);
        Window parentWindow = CreateWindow(parentScope, 42);
        Window childWindow = CreateWindow(childScope, 43);
        PixelyAppBuilder builder = new();
        builder.AddSingleton(parentWindow);
        ServiceProvider parent = builder.BuildServiceProvider();
        ServiceCollection childServices = parent.CreateServiceCollection();
        childServices.AddSingleton<WindowConsumer>(provider => new WindowConsumer(provider.GetWindow(childScope)));
        childServices.AddSingleton(childWindow);

        ServiceProvider child = childServices.BuildServiceProvider();
        WindowRegistry windowRegistry = parent.GetRequiredService<WindowRegistry>();

        Assert.Multiple(() =>
        {
            Assert.That(child.GetWindow(parentScope), Is.SameAs(parentWindow));
            Assert.That(child.GetRequiredService<WindowConsumer>().Window, Is.SameAs(childWindow));
            Assert.That(windowRegistry.GetWindow(childScope), Is.SameAs(childWindow));
        });
    }

    [Test]
    public void GetWindow_MissingScope_Throws()
    {
        ViewScope viewScope = new(7);
        ServiceCollection services = new();
        ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() => provider.GetWindow(viewScope));

        Assert.That(exception!.Message, Does.Contain("ViewScope 7"));
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

    private sealed record WindowConsumer(Window Window);
}
