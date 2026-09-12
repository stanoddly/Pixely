using Pixely.Content;
using Pixely.DependencyInjection;

namespace Pixely.Input;

public static class InputAutomationExtensions
{
    /// <summary>Needs an <see cref="OffscreenWindow"/>, registered by <c>UseOffscreenRendering()</c> or <c>AddOffscreenWindow()</c>, for the <c>screenshot</c> command.</summary>
    public static ServiceCollection AddInputAutomation(this ServiceCollection services)
    {
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
