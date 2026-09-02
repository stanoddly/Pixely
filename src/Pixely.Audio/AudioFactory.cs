using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Audio;

internal unsafe sealed class AudioFactory
{
    private readonly PixelyFactory _sdlLifetime;

    public AudioFactory(PixelyFactory sdlLifetime)
    {
        _sdlLifetime = sdlLifetime;
    }

    public AudioSystem CreateAudioSystem(ContentSource contentSource)
    {
        bool sdlAudioInitialized = false;
        bool mixerInitialized = false;
        Pointer<MIX_Mixer> mixer = Pointer<MIX_Mixer>.Null;

        try
        {
            _sdlLifetime.EnsureApplicationIdentifierConfigured();
            SdlError.ThrowOnFalse(
                SDL3.SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO),
                nameof(SDL3.SDL_InitSubSystem));
            sdlAudioInitialized = true;

            SdlError.ThrowOnFalse(
                SDL3_mixer.MIX_Init(),
                nameof(SDL3_mixer.MIX_Init));
            mixerInitialized = true;

            mixer = SDL3_mixer.MIX_CreateMixerDevice(SDL3.SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, null);
            SdlError.ThrowOnNull(mixer, nameof(SDL3_mixer.MIX_CreateMixerDevice));

            return new AudioSystem(_sdlLifetime, contentSource, mixer, sdlAudioInitialized, mixerInitialized);
        }
        catch
        {
            if (!mixer.IsNull)
            {
                SDL3_mixer.MIX_DestroyMixer(mixer);
            }

            if (mixerInitialized)
            {
                SDL3_mixer.MIX_Quit();
            }

            if (sdlAudioInitialized)
            {
                SDL3.SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            }

            throw;
        }
    }
}
