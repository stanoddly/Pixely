using Pixely.DependencyInjection;

namespace Pixely.Input;

public static class InputAutomationExtensions
{
    public static ServiceCollection AddInputAutomation(this ServiceCollection services)
    {
        services.AddSingleton<InputAutomation, PixelyFactory>();
        services.AddAlias<IInputAutomation, InputAutomation>();
        services.AddSingleton<InputAutomationConsole, PixelyFactory>();
        return services;
    }
}
