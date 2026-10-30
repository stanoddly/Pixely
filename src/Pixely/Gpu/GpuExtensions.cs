using Pixely.DependencyInjection;
using Pixely.Shaders;
using Pixely.Text;

namespace Pixely.Gpu;

public static class GpuExtensions
{
    // The device and every service that needs it. A registered device, whatever registered it, means the block is present.
    public static ServiceCollection UseGpu(this ServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.IsRegistered<GpuDevice>())
        {
            return services;
        }

        services.AddSingleton<GpuDevice, PixelyFactory>();
        AddGpuServices(services);
        return services;
    }

    // Every service that needs the device, for a registration that has already registered the device, such as Pixely.Testing's stub.
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
