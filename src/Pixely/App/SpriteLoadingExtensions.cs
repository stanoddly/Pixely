using Pixely.DependencyInjection;
using Pixely.Sprites;

namespace Pixely.App;

public static class SpriteLoadingExtensions
{
    public static ServiceCollection RegisterSpriteLoading(this ServiceCollection services)
    {
        services.AddSingleton<SpriteAssetStorage>();
        services.AddSingleton<ISpriteAssetLoader, SpriteAssetLoader>();
        services.AddSingleton<IAnimatedSpriteAssetLoader, AnimatedSpriteAssetLoader>();
        return services;
    }

    public static ServiceCollection RegisterAtlas(this ServiceCollection services, params string[] paths)
    {
        services.AddSingleton(new SpriteAtlasBuilderConfig(paths));
        services.AddSingleton<SpriteAtlasBuilder>(SpriteAtlasBuilder.Create);
        return services;
    }
}
