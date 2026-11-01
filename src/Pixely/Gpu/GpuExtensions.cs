using Pixely.DependencyInjection;
using Pixely.Shaders;
using Pixely.Text;

namespace Pixely.Gpu;

internal static class GpuExtensions
{
    // Every service that needs the device. PixelyAppBuilder.Build() registers them when they are missing, and Pixely.Testing registers
    // them next to its stub device.
    internal static void AddGpuServices(ServiceCollection services)
    {
        services.AddSingleton<GpuMemorySystem, PixelyFactory>();

        services.AddSingleton<ShaderLoader>();
        services.AddAlias<IShaderLoader, ShaderLoader>();

        services.AddSingleton<ITextureLoader, TextureLoader>();

        services.AddSingleton<GraphicsPipelineBuilder>();

        services.AddSingleton<ComputeShaderLoader>();
        services.AddAlias<IComputeShaderLoader, ComputeShaderLoader>();

        services.AddSingleton<ComputePipelineBuilder>();

        services.AddSingleton<FontSystem>(FontSystem.Create);
        services.AddAlias<IFontSystem, FontSystem>();
    }
}
