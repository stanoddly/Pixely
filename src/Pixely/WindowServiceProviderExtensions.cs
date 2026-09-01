using Pixely.DependencyInjection;

namespace Pixely;

public static class WindowServiceProviderExtensions
{
    /// <summary>
    /// Resolves the window registered for a view scope.
    /// </summary>
    /// <remarks>
    /// Resolving through the provider activates the window if it has not been activated yet, which
    /// <see cref="WindowRegistry"/> cannot do: the registry is populated by an activation callback,
    /// so it is empty until something else has already resolved the window. Services that need a
    /// window while they are being constructed must use this method, not the registry.
    /// </remarks>
    public static Window GetWindow(this ServiceProvider provider, ViewScope viewScope = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        foreach (Window window in provider.GetServices<Window>())
        {
            if (window.ViewScope == viewScope)
            {
                return window;
            }
        }

        throw new InvalidOperationException($"No window is registered for ViewScope {viewScope.Value}.");
    }
}
