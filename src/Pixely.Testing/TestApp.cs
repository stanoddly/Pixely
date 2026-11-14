using Pixely.App;
using Pixely.Audio;
using Pixely.Gpu;
using Pixely.Headless;
using Pixely.Input;
using Pixely.Text;

namespace Pixely.Testing;

public static class TestApp
{
    /// <summary>
    /// Creates a builder for a game's tests, which a test passes to the game's own <c>Configure</c> before <c>Build()</c>. The app
    /// never starts SDL, so apps can run in parallel, each on one thread at a time. It renders on a <see cref="StubGpuDevice"/>,
    /// with every window a <see cref="StubWindow"/>, measures text with a <see cref="StubFontSystem"/>, plays sound on a
    /// <see cref="StubAudioSystem"/>, has a display, cursors, a clipboard and platform settings of its own, ignores the machine's
    /// gamepads, and advances game time by 1/30 s per frame. <see cref="TestInput"/> sends it synthetic input, and no console reads
    /// standard input, so <c>AddHeadlessSupport()</c> and <c>PIXELY_COMMAND_CONSOLE</c> add nothing.
    /// </summary>
    public static PixelyAppBuilder CreateBuilder(StubGpuDeviceOptions? options = null)
    {
        PixelyAppBuilder builder = new();
        // Registered before the game's Configure, whose UseGpu then keeps the stub. The DI container creates every singleton at
        // build, also one a later registration hides, so a stub registered after UseGpu would leave the SDL device created.
        builder.AddSingleton<StubGpuDevice>(_ => StubGpuDevice.Create(options));
        builder.AddAlias<GpuDevice, StubGpuDevice>();
        builder.AddSingleton<StubEventSource>(static provider => new StubEventSource(provider.GetRequiredService<FrameClock>()));
        builder.AddAlias<IEventSource, StubEventSource>();
        builder.AddSingleton<IWindowFactory>(static provider => new StubWindowFactory(provider.GetRequiredService<StubGpuDevice>(), provider.GetRequiredService<StubEventSource>()));
        // Before AddGpuServices, which then keeps it.
        builder.AddSingleton<StubFontSystem>(static provider => new StubFontSystem(gpuDevice: provider.GetRequiredService<StubGpuDevice>()));
        builder.AddAlias<IFontSystem, StubFontSystem>();
        GpuExtensions.AddGpuServices(builder);

        // Registered before the game's Configure, whose RegisterAudio then keeps the stub.
        builder.AddSingleton<StubAudioSystem>(static _ => new StubAudioSystem());
        builder.AddAlias<AudioSystem, StubAudioSystem>();
        builder.AddAlias<IAudioSystem, AudioSystem>();

        // Registered before the game's Configure, and PixelyAppBuilder.Build then registers none of SDL's.
        builder.AddSingleton(new PlatformInfo((string?)null));
        builder.AddSingleton<ICursorService>(static _ => new StubCursorService());
        builder.AddSingleton<IClipboardService>(static _ => new StubClipboardService());
        builder.AddSingleton<IPlatformSettings>(static _ => new StubPlatformSettings());
        builder.AddSingleton<IDisplayService>(static _ => new StubDisplayService());

        // Registered after PixelyAppBuilder applies the PIXELY_* variables, so it runs after them.
        builder.OnActivated(static (instance, _) =>
        {
            if (instance is PixelyConfig config)
            {
                config.IsIsolated = true;
                config.EnableCommandConsole = false;
            }
        });

        builder.AddSingleton<PixelyFrameClock>(static _ => new FixedStepFrameClock());
        builder.AddSingleton<InputAutomation>(static provider => new InputAutomation(provider.GetRequiredService<WindowRegistry>(),
            provider.GetRequiredService<MouseService>(), provider.GetRequiredService<KeyboardService>(), provider.GetRequiredService<TextInputService>(),
            provider.GetRequiredService<GamepadService>(), provider.GetRequiredService<FrameClock>()));
        builder.AddSingleton<TestInput>(static provider => new TestInput(provider.GetRequiredService<InputAutomation>()));
        return builder;
    }
}
