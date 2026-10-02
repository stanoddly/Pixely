using Pixely.Collections;
using Pixely.DependencyInjection;

namespace Pixely;

public sealed class WindowRegistry
{
    private FastListStruct<(ViewScope ViewScope, uint SdlId, Window Window)> _windows = new(4);

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
        ReadOnlySpan<(ViewScope ViewScope, uint SdlId, Window Window)> windows =
            _windows.AsReadOnlySpan();
        foreach ((ViewScope registeredViewScope, _, Window registeredWindow) in windows)
        {
            if (registeredViewScope == viewScope)
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
        ReadOnlySpan<(ViewScope ViewScope, uint SdlId, Window Window)> windows =
            _windows.AsReadOnlySpan();
        foreach ((_, uint registeredSdlId, Window registeredWindow) in windows)
        {
            if (registeredSdlId == sdlWindowId)
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
        ViewScope viewScope = window.ViewScope;
        uint sdlId = window.SdlId;
        ReadOnlySpan<(ViewScope ViewScope, uint SdlId, Window Window)> windows =
            _windows.AsReadOnlySpan();
        foreach ((
            ViewScope registeredViewScope,
            uint registeredSdlId,
            Window registeredWindow) in windows)
        {
            if (ReferenceEquals(registeredWindow, window))
            {
                return;
            }

            if (registeredViewScope == viewScope)
            {
                throw new InvalidOperationException(
                    $"A window for ViewScope {viewScope.Value} is already registered.");
            }

            if (registeredSdlId == sdlId)
            {
                throw new InvalidOperationException(
                    $"SDL window ID {sdlId} is already registered.");
            }
        }

        _windows.Add((viewScope, sdlId, window));
    }

    internal void Unregister(Window window)
    {
        ReadOnlySpan<(ViewScope ViewScope, uint SdlId, Window Window)> windows =
            _windows.AsReadOnlySpan();
        for (int i = 0; i < windows.Length; i++)
        {
            if (ReferenceEquals(windows[i].Window, window))
            {
                _windows.SwapRemove(i);
                return;
            }
        }
    }
}
