using Microsoft.Extensions.Logging;
using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.App;

public class PixelyAppBuilder : ServiceCollection
{
    private readonly ContentSourceBuilder _contentSourceBuilder = new();
    private WindowConfig _windowConfig = new();

    public PixelyAppBuilder()
    {
        // Applied to the app's PixelyConfig registration (or the default from Build()) before any service sees it.
        OnActivated(static (instance, type) =>
        {
            if (type == typeof(PixelyConfig))
            {
                PixelyConfigEnvironment.Apply((PixelyConfig)instance, Environment.GetEnvironmentVariable);
            }
        });
        AddSingleton<ContentSource>(() => _contentSourceBuilder.Create());
        AddRegistry<IRenderCoordinator>();
        AddRegistry<IRenderer<BasicRenderContext>>(static renderer => renderer.RenderOrder);
        AddRegistry<IUpdatable>(static updatable => updatable.UpdateOrder);

        // Stage providers inherit these callbacks, so a stage's commands join on its load and leave on its unload.
        AutomationCommandRegistry automationCommands = new();
        AddSingleton(automationCommands);
        OnActivated((instance, _) =>
        {
            if (instance is AutomationCommand command)
            {
                automationCommands.Add(command);
            }
        });
        OnDisposing((instance, _) =>
        {
            if (instance is AutomationCommand command)
            {
                automationCommands.Remove(command);
            }
        });
    }

    public PixelyAppBuilder ConfigureContent(Action<ContentSourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_contentSourceBuilder);
        return this;
    }

    // The app's one window, which Build() creates. A later call replaces the settings of an earlier one.
    public PixelyAppBuilder ConfigureWindow(WindowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        _windowConfig = config;
        return this;
    }

    public PixelyAppBuilder UseDefaultContent(string contentDirectory = "Content")
    {
        return ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddDefaultContent(contentDirectory));
    }

    public IPixelyApp Build()
    {
        if (!IsRegistered<PixelyConfig>())
        {
            AddSingleton(new PixelyConfig());
        }
        AddSingleton<PixelyFactory>();
        AddSingleton<SdlLibrary>(SdlLibrary.Load);

        // Every app has one GPU device and one window. A device registered before Build(), such as the stub of Pixely.Testing, is
        // kept: the container creates every singleton at build, also one a later registration hides, so registering the SDL device
        // next to it would create both. The GPU services are registered whenever they are missing, also next to such a device.
        if (!IsRegistered<GpuDevice>())
        {
            AddSingleton<GpuDevice, PixelyFactory>();
        }

        if (!IsRegistered<GpuMemorySystem>())
        {
            GpuExtensions.AddGpuServices(this);
        }

        WindowConfig windowConfig = _windowConfig;
        AddSingleton<Window>(provider => CreateWindow(provider, windowConfig));

        AddSingleton<PlatformInfo, PixelyFactory>();

        AddSingleton<KeyboardService, PixelyFactory>();
        AddAlias<IKeyboardService, KeyboardService>();

        AddSingleton<GamepadService, PixelyFactory>();
        AddAlias<IGamepadService, GamepadService>();

        AddSingleton<MouseService, PixelyFactory>();
        AddAlias<IMouseService, MouseService>();

        AddSingleton<CursorService, PixelyFactory>();
        AddAlias<ICursorService, CursorService>();

        AddSingleton<TextInputService, PixelyFactory>();
        AddAlias<ITextInputService, TextInputService>();

        AddSingleton<ClipboardService>();
        AddAlias<IClipboardService, ClipboardService>();

        AddSingleton<PlatformSettings, PixelyFactory>();
        AddAlias<IPlatformSettings, PlatformSettings>();

        AddSingleton<EventService, PixelyFactory>();

#if !BROWSER
        // PixelyConfig.EnableCommandConsole only; the factory returns null otherwise.
        AddSingleton<AutomationCommandConsole, PixelyFactory>();
#endif

        AddSingleton<GraphicsShaderProgramMetadataLoader>();

        AddSingleton<ComputeShaderMetadataLoader>();

        // Headless mode (Pixely.Headless) registers a clock of its own, which the latest registration would otherwise replace.
        if (!IsRegistered<PixelyFrameClock>())
        {
            AddSingleton<PixelyFrameClock>(static _ => new SdlFrameClock());
        }

        AddAlias<FrameClock, PixelyFrameClock>();

        AddSingleton<AppControl>();
        AddSingleton<UpdateSystem>();
        AddSingleton<TimerSystem>();

        AddSingleton<StageManager>();
        AddAlias<IStageManager, StageManager>();

        if (!IsRegistered<IImageLoader>())
        {
            AddSingleton<IImageLoader, SdlImageLoader>();
        }

        if (!IsRegistered<IImageDecoder>())
        {
            AddSingleton<IImageDecoder, SdlImageDecoder>();
        }

        // Diagnostics, see docs/diagnostics.md. Registered last, and the report resolves the logger factory, the GPU device and the
        // GPU memory system only while diagnostics are on, so they change neither when the app's services are created nor when they
        // are disposed. While they are off, the frame records its timings to a recorder that does nothing and no report is created.
        AddSingleton<FrameTimings>(static provider => provider.GetRequiredService<PixelyConfig>().EnableDiagnostics ? new FrameTimings() : null);
        AddSingleton<IFrameTimingRecorder>(static provider => provider.GetService<FrameTimings>() ?? (IFrameTimingRecorder)NullFrameTimingRecorder.Instance);
        AddSingleton<PerformanceReport>(static provider => provider.GetService<FrameTimings>() is { } timings
            ? new PerformanceReport(timings, provider.GetService<ILoggerFactory>()?.CreateLogger(PerformanceReport.LoggerCategoryName), provider.GetService<GpuDevice>(),
                provider.GetService<GpuMemorySystem>()?.WaitsForUploads)
            : null);

        ServiceProvider serviceProvider = BuildServiceProvider();
        return new PixelyApp(
            serviceProvider,
            serviceProvider.GetRequiredService<PixelyFrameClock>(),
            serviceProvider.GetRequiredService<EventService>(),
            serviceProvider.GetRequiredService<AppControl>(),
            serviceProvider.GetRequiredService<ServiceRegistry<IRenderCoordinator>>(),
            serviceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>(),
            serviceProvider.GetRequiredService<StageManager>(),
            serviceProvider.GetRequiredService<IFrameTimingRecorder>(),
            !serviceProvider.GetRequiredService<PixelyConfig>().IsIsolated);
    }

    private static Window CreateWindow(ServiceProvider provider, WindowConfig config)
    {
        GpuDevice gpuDevice = provider.GetRequiredService<GpuDevice>();
        PixelyFrameClock frameClock = provider.GetRequiredService<PixelyFrameClock>();
        PlatformInfo platformInfo = provider.GetRequiredService<PlatformInfo>();

        // Two factories mean two kinds of window, such as headless mode's offscreen one and the stub GPU device's, and neither works
        // on the other's terms, so picking one would hide the conflict.
        IReadOnlyList<IWindowFactory> windowFactories = provider.GetServices<IWindowFactory>();
        if (windowFactories.Count > 1)
        {
            throw new PixelyInitializationException(
                $"An app's window comes from one place, but {string.Join(" and ", windowFactories.Select(static factory => factory.GetType().Name))} are both registered to create it.");
        }

        if (windowFactories.Count == 1)
        {
            return windowFactories[0].CreateWindow(gpuDevice, frameClock, config, platformInfo);
        }

        return provider.GetRequiredService<PixelyFactory>().CreateWindow(gpuDevice, frameClock, config, platformInfo, provider.GetRequiredService<IImageLoader>());
    }
}
