using Pixely.App;
using Pixely.Gpu;
using Pixely.Headless;
using Pixely.Input;

namespace Pixely.Testing;

public static class TestApp
{
    /// <summary>
    /// Creates a builder for a game's tests, which a test passes to the game's own <c>Configure</c> before <c>Build()</c>. The app
    /// renders on a <see cref="StubGpuDevice"/> into a <see cref="StubWindow"/>, uses SDL's dummy video and audio
    /// drivers unless <c>SDL_VIDEO_DRIVER</c> or <c>SDL_AUDIO_DRIVER</c> picks another, ignores the machine's gamepads and the OS's
    /// theme, and advances game time by 1/30 s per frame. <see cref="TestInput"/> sends it synthetic input, and no console reads
    /// standard input, so <c>AddHeadlessSupport()</c> and <c>PIXELY_COMMAND_CONSOLE</c> add nothing.
    /// </summary>
    public static PixelyAppBuilder CreateBuilder(StubGpuDeviceOptions? options = null)
    {
        PixelyAppBuilder builder = new();
        // Registered before Build(), which then keeps the stub and its GPU services. The DI container creates every singleton at build,
        // also one a later registration hides, so registering the SDL device next to the stub would create both.
        builder.AddSingleton<StubGpuDevice>(_ => StubGpuDevice.Create(options));
        builder.AddAlias<GpuDevice, StubGpuDevice>();
        builder.AddSingleton<IWindowFactory>(static provider => new StubWindowFactory(provider.GetRequiredService<PixelyFactory>()));
        GpuExtensions.AddGpuServices(builder);

        // Registered after PixelyAppBuilder applies the PIXELY_* variables, so it runs after them.
        builder.OnActivated(static (instance, _) =>
        {
            if (instance is PixelyConfig config)
            {
                config.SdlVideoDriver ??= "dummy";
                config.SdlAudioDriver ??= "dummy";
                config.IsIsolated = true;
                config.EnableCommandConsole = false;
            }
        });

        builder.AddSingleton<PixelyFrameClock>(static _ => new FixedStepFrameClock());
        builder.AddSingleton<InputAutomation>(static provider => new InputAutomation(provider.GetRequiredService<MouseService>(), provider.GetRequiredService<KeyboardService>(), provider.GetRequiredService<TextInputService>(),
            provider.GetRequiredService<GamepadService>(), provider.GetRequiredService<FrameClock>()));
        builder.AddSingleton<TestInput>(static provider => new TestInput(provider.GetRequiredService<InputAutomation>()));
        return builder;
    }
}
