using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.App;

public class PixelyAppBuilder : ServiceCollection
{
    private readonly ContentSourceBuilder _contentSourceBuilder = new();

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
        WindowRegistry.AddWindowRegistry(this);
        AddRegistry<IRenderCoordinator>();
        AddRegistry<IRenderer<BasicRenderContext>>(static renderer => renderer.RenderOrder);
        AddRegistry<IUpdatable>(static updatable => updatable.UpdateOrder);
    }

    public PixelyAppBuilder ConfigureContent(Action<ContentSourceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_contentSourceBuilder);
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

        AddSingleton<PlatformInfo, PixelyFactory>();

        AddSingleton<KeyboardService, PixelyFactory>();
        AddAlias<IKeyboardService, KeyboardService>();

        AddSingleton<GamepadService, PixelyFactory>();
        AddAlias<IGamepadService, GamepadService>();

        AddSingleton<MouseService, PixelyFactory>();
        AddAlias<IMouseService, MouseService>();

        AddSingleton<TextInputService, PixelyFactory>();
        AddAlias<ITextInputService, TextInputService>();

        AddSingleton<ClipboardService>();
        AddAlias<IClipboardService, ClipboardService>();

        AddSingleton<EventService, PixelyFactory>();

        // Headless mode only, see PixelyConfig.Headless; the factories return null otherwise.
        AddSingleton<InputAutomation, PixelyFactory>();
        if (!IsRegistered<IImageWriter>())
        {
            AddSingleton<IImageWriter, PixelyFactory>();
        }
#if !BROWSER
        AddSingleton<InputAutomationConsole, PixelyFactory>();
#endif

        AddSingleton<GraphicsShaderProgramMetadataLoader>();

        AddSingleton<ComputeShaderMetadataLoader>();

        AddSingleton<PixelyFrameContext>();
        AddAlias<FrameContext, PixelyFrameContext>();

        AddSingleton<AppControl>();
        AddSingleton<UpdateSystem>();
        AddSingleton<TimerSystem>();

        AddSingleton<StageManager>();
        AddAlias<IStageManager, StageManager>();

        if (!IsRegistered<IImageLoader>())
        {
            AddSingleton<IImageLoader, SdlImageLoader>();
        }

        ServiceProvider serviceProvider = BuildServiceProvider();
        return new PixelyApp(
            serviceProvider,
            serviceProvider.GetRequiredService<PixelyFrameContext>(),
            serviceProvider.GetRequiredService<EventService>(),
            serviceProvider.GetRequiredService<AppControl>(),
            serviceProvider.GetRequiredService<ServiceRegistry<IRenderCoordinator>>(),
            serviceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>(),
            serviceProvider.GetRequiredService<StageManager>());
    }
}
