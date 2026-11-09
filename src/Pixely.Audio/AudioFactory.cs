using Pixely.Content;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

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
            SdlError.ThrowOnFalse(
                SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO),
                nameof(SDL_InitSubSystem));
            sdlAudioInitialized = true;

            SdlError.ThrowOnFalse(
                MIX_Init(),
                nameof(MIX_Init));
            mixerInitialized = true;

            mixer = MIX_CreateMixerDevice(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, null);
            SdlError.ThrowOnNull(mixer, nameof(MIX_CreateMixerDevice));

            return new AudioSystem(_sdlLifetime, contentSource, mixer, sdlAudioInitialized, mixerInitialized);
        }
        catch
        {
            if (!mixer.IsNull)
            {
                MIX_DestroyMixer(mixer);
            }

            if (mixerInitialized)
            {
                MIX_Quit();
            }

            if (sdlAudioInitialized)
            {
                SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            }

            throw;
        }
    }
}
