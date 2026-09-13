using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;

namespace Pixely;

public static class HeadlessExtensions
{
    /// <summary>
    /// Runs the app without a display: the default scope's window is an <see cref="OffscreenWindow"/> that renders into a texture,
    /// and the app is driven through <see cref="IInputAutomation"/> and the standard-input command protocol, screenshots included.
    /// Call it before <c>UseDefaultRendering()</c>, which then keeps this window instead of creating its own, or before
    /// <c>UseWindowRendering&lt;TRenderContext&gt;()</c> for a custom render context. Only the size is configurable.
    /// </summary>
    public static ServiceCollection UseHeadless(this ServiceCollection services, Size<uint>? size = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<OffscreenWindow>(provider =>
            provider.GetRequiredService<PixelyFactory>().CreateOffscreenWindow(
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<PixelyFrameContext>(),
                provider.GetRequiredService<PlatformInfo>(),
                size));
        services.AddAlias<Window, OffscreenWindow>();

        if (!services.IsRegistered<IImageWriter>())
        {
            services.AddSingleton<IImageWriter, SdlImageWriter>();
        }

        services.AddSingleton<InputAutomation, PixelyFactory>();
        services.AddAlias<IInputAutomation, InputAutomation>();
        services.AddSingleton<InputAutomationConsole, PixelyFactory>();
        return services;
    }
}
