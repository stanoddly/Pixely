using Pixely.DependencyInjection;

namespace Pixely.Models;

public static class ModelLoadingExtensions
{
    public static ServiceCollection RegisterModelLoading(this ServiceCollection services)
    {
        services.AddSingleton<IModelLoader, ModelLoader>();
        services.AddSingleton<ModelInstanceFactory>();
        return services;
    }
}
