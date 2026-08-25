using Pixely.DependencyInjection;

namespace Pixely.Audio;

public static class AudioServiceCollectionExtensions
{
    public static ServiceCollection RegisterAudio(this ServiceCollection services)
    {
        services.AddSingleton<AudioFactory>();
        services.AddSingleton<AudioSystem, AudioFactory>();
        services.AddAlias<IAudioSystem, AudioSystem>();
        return services;
    }
}
