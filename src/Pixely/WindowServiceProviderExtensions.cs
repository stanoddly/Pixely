using Pixely.DependencyInjection;

namespace Pixely;

public static class WindowServiceProviderExtensions
{
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
