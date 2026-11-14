using Pixely.Collections;
using Pixely.DependencyInjection;

namespace Pixely;

public sealed class WindowRegistry
{
    private FastListStruct<Window> _windows = new(4);

    internal WindowRegistry()
    {
    }

    internal static void AddWindowRegistry(ServiceCollection services)
    {
        WindowRegistry windowRegistry = new();
        services.AddSingleton(windowRegistry);
        
        services.OnActivated((instance, _) =>
        {
            if (instance is Window window)
            {
                windowRegistry.Register(window);
            }
        });
        services.OnDisposing((instance, _) =>
        {
            if (instance is Window window)
            {
                windowRegistry.Unregister(window);
            }
        });
    }

    public Window GetWindow(ViewScope viewScope = default)
    {
        if (TryGetWindow(viewScope, out Window window))
        {
            return window;
        }

        throw new InvalidOperationException(
            $"No active window is registered for ViewScope {viewScope.Value}.");
    }

    public bool TryGetWindow(ViewScope viewScope, out Window window)
    {
        foreach (Window registeredWindow in _windows.AsReadOnlySpan())
        {
            if (registeredWindow.ViewScope == viewScope)
            {
                window = registeredWindow;
                return true;
            }
        }

        window = null!;
        return false;
    }

    public bool TryGetWindow(out Window window)
    {
        return TryGetWindow(default(ViewScope), out window);
    }

    internal bool TryGetWindow(uint sdlWindowId, out Window window)
    {
        foreach (Window registeredWindow in _windows.AsReadOnlySpan())
        {
            if (registeredWindow is NativeWindow nativeWindow && nativeWindow.SdlId == sdlWindowId)
            {
                window = registeredWindow;
                return true;
            }
        }

        window = null!;
        return false;
    }

    internal void Register(Window window)
    {
        foreach (Window registeredWindow in _windows.AsReadOnlySpan())
        {
            if (ReferenceEquals(registeredWindow, window))
            {
                return;
            }

            if (registeredWindow.ViewScope == window.ViewScope)
            {
                throw new InvalidOperationException(
                    $"A window for ViewScope {window.ViewScope.Value} is already registered.");
            }

            if (registeredWindow is NativeWindow registeredNativeWindow && window is NativeWindow nativeWindow && registeredNativeWindow.SdlId == nativeWindow.SdlId)
            {
                throw new InvalidOperationException(
                    $"SDL window ID {nativeWindow.SdlId} is already registered.");
            }
        }

        _windows.Add(window);
    }

    internal void Unregister(Window window)
    {
        ReadOnlySpan<Window> windows = _windows.AsReadOnlySpan();
        for (int i = 0; i < windows.Length; i++)
        {
            if (ReferenceEquals(windows[i], window))
            {
                _windows.SwapRemove(i);
                return;
            }
        }
    }
}
