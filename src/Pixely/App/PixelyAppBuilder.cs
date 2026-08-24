using Pixely.Content;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Shaders;
using Pixely.Text;

namespace Pixely.App;

public class PixelyAppBuilder : ServiceCollection
{
    public PixelyAppBuilder()
    {
        ContentServiceCollectionExtensions.EnsureFileSystemRegistration(this);
        WindowRegistry.AddWindowRegistry(this);
        AddRegistry<IRenderCoordinator>();
        AddRegistry<IRenderer<DefaultRenderContext>>(static (left, right) => left.Order.CompareTo(right.Order));
        AddRegistry<IUpdatable>(static (left, right) =>
        {
            int leftOrder = left is IOrderable leftOrderable ? leftOrderable.Order : 0;
            int rightOrder = right is IOrderable rightOrderable ? rightOrderable.Order : 0;
            return leftOrder.CompareTo(rightOrder);
        });
    }

    public IPixelyApp Build()
    {
        if (!IsRegistered<PixelyConfig>())
        {
            AddSingleton(new PixelyConfig());
        }
        AddSingleton<PixelyFactory>();

        AddSingleton<PlatformInfo, PixelyFactory>();

        AddSingleton<GpuDevice, PixelyFactory>();

        AddSingleton<GpuMemorySystem>();

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

        AddSingleton<GraphicsShaderProgramMetadataLoader>();

        AddSingleton<ShaderLoader>();
        AddAlias<IShaderLoader, ShaderLoader>();

        AddSingleton<ITextureLoader, TextureLoader>();

        AddSingleton<GraphicsPipelineBuilder>();

        AddSingleton<ComputeShaderMetadataLoader>();

        AddSingleton<ComputeShaderLoader>();
        AddAlias<IComputeShaderLoader, ComputeShaderLoader>();

        AddSingleton<ComputePipelineBuilder>();

        AddSingleton<PixelyFrameContext>();
        AddAlias<FrameContext, PixelyFrameContext>();

        AddSingleton<FontSystem>(FontSystem.Create);
        AddAlias<IFontSystem, FontSystem>();

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
        return new PixelyApp(serviceProvider);
    }
}
