using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;

namespace Pixely;

public static class HeadlessWindowServiceCollectionExtensions
{
    public static ServiceCollection AddHeadlessWindow(this ServiceCollection services, WindowConfig? config = null)
    {
        return AddHeadlessWindow(services, default, config);
    }

    /// <summary>
    /// Registers an <see cref="OffscreenWindow"/> as the scope's <see cref="Window"/>, the headless counterpart of
    /// <see cref="WindowServiceCollectionExtensions.AddWindow(ServiceCollection, ViewScope, WindowConfig?)"/>. Only
    /// <see cref="WindowConfig.Size"/> and <see cref="WindowConfig.Title"/> apply; the rest of the config is about the desktop.
    /// The first call also registers <see cref="IInputAutomation"/> and the standard-input command console.
    /// </summary>
    public static ServiceCollection AddHeadlessWindow(this ServiceCollection services, ViewScope viewScope, WindowConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<Window>(provider =>
            provider.GetRequiredService<PixelyFactory>().CreateOffscreenWindow(
                viewScope,
                provider.GetRequiredService<GpuDevice>(),
                provider.GetRequiredService<PixelyFrameContext>(),
                provider.GetRequiredService<PlatformInfo>(),
                config ?? new WindowConfig()));

        if (services.IsRegistered<InputAutomationConsole>())
        {
            return services;
        }

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
