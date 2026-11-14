using Pixely.DependencyInjection;

namespace Pixely.Audio;

public static class AudioServiceCollectionExtensions
{
    // A registered audio system, whatever registered it, such as Pixely.Testing's stub, means audio is present.
    public static ServiceCollection RegisterAudio(this ServiceCollection services)
    {
        if (services.IsRegistered<AudioSystem>())
        {
            return services;
        }

        services.AddSingleton<AudioFactory>();
        services.AddSingleton<AudioSystem, AudioFactory>();
        services.AddAlias<IAudioSystem, AudioSystem>();
        return services;
    }
}
