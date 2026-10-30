using Pixely.App;
using Pixely.Input;

namespace Pixely.Headless;

public static class HeadlessExtensions
{
    internal const string HeadlessVariable = "PIXELY_HEADLESS";

    /// <summary>
    /// Lets the environment variable <c>PIXELY_HEADLESS</c> run the app in headless mode (<c>docs/headless.md</c>): hidden windows
    /// that render into GPU textures, synthetic input and game commands from standard input, screenshots, and a fixed-step clock.
    /// Without the variable it adds nothing, so the app runs as usual. The generated <c>Main</c> of a hosted app
    /// (<c>PixelyHosting</c>) calls it in a Debug build; a Release build that wants headless mode calls it in <c>Configure</c>.
    /// </summary>
    public static PixelyAppBuilder AddHeadlessSupport(this PixelyAppBuilder builder)
    {
        return AddHeadlessSupport(builder, Environment.GetEnvironmentVariable);
    }

    internal static PixelyAppBuilder AddHeadlessSupport(this PixelyAppBuilder builder, Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!PixelyConfigEnvironment.ResolveBoolean(false, HeadlessVariable, getVariable(HeadlessVariable)))
        {
            return builder;
        }

        // A second call, such as one in a hosted app's Configure after the generated Main's, would register a second console on
        // standard input and a second window factory.
        if (builder.IsRegistered<InputAutomationConsole>())
        {
            return builder;
        }

        // Registered after PixelyAppBuilder applies the PIXELY_* variables, so it runs after them. Headless mode reads standard
        // input with a console of its own, so the command console of PIXELY_COMMAND_CONSOLE stays off.
        builder.OnActivated(static (instance, _) =>
        {
            if (instance is PixelyConfig config)
            {
                config.IsIsolated = true;
                config.EnableCommandConsole = false;
            }
        });

        builder.AddSingleton<PixelyFrameClock>(static _ => new FixedStepFrameClock());
        builder.AddSingleton<IWindowFactory>(static provider => new OffscreenWindowFactory(provider.GetRequiredService<PixelyFactory>()));
        builder.AddSingleton<InputAutomation>(static provider => new InputAutomation(provider.GetRequiredService<WindowRegistry>(),
            provider.GetRequiredService<MouseService>(), provider.GetRequiredService<KeyboardService>(), provider.GetRequiredService<TextInputService>(),
            provider.GetRequiredService<GamepadService>(), provider.GetRequiredService<FrameClock>()));

        // A writer the app registered before stays; one registered after replaces this one, as the latest registration does.
        if (!builder.IsRegistered<IImageWriter>())
        {
            builder.AddSingleton<IImageWriter>(static _ => new SdlImageWriter());
        }

        // Raw standard streams, so reading never changes the terminal mode the way Console.In does on Unix.
        builder.AddSingleton<InputAutomationConsole>(static provider => new InputAutomationConsole(
            new InputAutomationCommandInterpreter(provider.GetRequiredService<InputAutomation>(), provider.GetRequiredService<WindowRegistry>(),
                provider.GetRequiredService<IImageWriter>(), provider.GetRequiredService<AppControl>(), provider.GetRequiredService<AutomationCommandRegistry>()),
            new StreamReader(Console.OpenStandardInput()), provider.GetRequiredService<AppControl>(), provider.GetRequiredService<IFrameTimingRecorder>()));
        return builder;
    }
}
