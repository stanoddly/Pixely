using Pixely.DependencyInjection;
using Pixely.Gpu;

namespace Pixely.Models;

public static class ModelLoadingExtensions
{
    public static ServiceCollection RegisterModelLoading(this ServiceCollection services)
    {
        services.UseGpu();
        services.AddSingleton<IModelLoader, ModelLoader>();
        return services;
    }
}
