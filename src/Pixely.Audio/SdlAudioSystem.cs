using System.Collections.Concurrent;
using System.Numerics;
using Pixely.Content;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Audio;

// The audio system on SDL_mixer and its device mixer.
internal unsafe sealed class SdlAudioSystem : AudioSystem
{
    private readonly PixelyFactory _sdlLifetime;
    private readonly ContentSource _contentSource;
    private readonly ConcurrentDictionary<nint, SdlAudioStream> _streams = new();
    private Pointer<MIX_Mixer> _mixer;
    private bool _sdlAudioInitialized;
    private bool _mixerInitialized;
    private long _lastStreamHandle;

    internal SdlAudioSystem(
        PixelyFactory sdlLifetime,
        ContentSource contentSource,
        Pointer<MIX_Mixer> mixer,
        bool sdlAudioInitialized,
        bool mixerInitialized)
    {
        _sdlLifetime = sdlLifetime;
        _contentSource = contentSource;
        _mixer = mixer;
        _sdlAudioInitialized = sdlAudioInitialized;
        _mixerInitialized = mixerInitialized;
    }

    internal override Pointer<MIX_Audio> LoadAudioCore(ReadOnlySpan<char> path)
    {
        using Stream fileStream = _contentSource.OpenStream(path);
        using MemoryStream memoryStream = new();
        fileStream.CopyTo(memoryStream);
        byte[] fileData = memoryStream.ToArray();

        fixed (byte* fileDataPointer = fileData)
        {
            Pointer<SDL_IOStream> ioStream = SDL_IOFromConstMem((IntPtr)fileDataPointer, (UIntPtr)fileData.Length);
            SdlError.ThrowOnNull(ioStream, nameof(SDL_IOFromConstMem));

            Pointer<MIX_Audio> sdlAudio = MIX_LoadAudio_IO(_mixer, ioStream, true, true);
            SdlError.ThrowOnNull(sdlAudio, nameof(MIX_LoadAudio_IO));
            return sdlAudio;
        }
    }

    internal override void DestroyAudioCore(Pointer<MIX_Audio> audio)
    {
        MIX_DestroyAudio(audio);
    }

    internal override nint OpenStreamCore(ReadOnlySpan<char> path)
    {
        Stream fileStream = _contentSource.OpenStream(path);
        nint handle = (nint)Interlocked.Increment(ref _lastStreamHandle);
        _streams[handle] = new SdlAudioStream(fileStream);
        return handle;
    }

    internal override void ReleaseStreamCore(nint stream)
    {
        if (_streams.TryRemove(stream, out SdlAudioStream? sdlStream))
        {
            sdlStream.Close();
        }
    }

    internal override Pointer<MIX_Track> CreateTrackCore()
    {
        Pointer<MIX_Track> track = MIX_CreateTrack(_mixer);
        SdlError.ThrowOnNull(track, nameof(MIX_CreateTrack));
        return track;
    }

    internal override void DestroyTrackCore(Pointer<MIX_Track> track)
    {
        MIX_DestroyTrack(track);
    }

    internal override void SetTrackAudioCore(Pointer<MIX_Track> track, Pointer<MIX_Audio> audio)
    {
        SdlError.ThrowOnFalse(MIX_SetTrackAudio(track, audio), nameof(MIX_SetTrackAudio));
    }

    internal override void SetTrackStreamCore(Pointer<MIX_Track> track, nint stream)
    {
        Pointer<SDL_IOStream> ioStream = stream == 0 ? Pointer<SDL_IOStream>.Null : _streams[stream].GetOrCreateIoStream();
        SdlError.ThrowOnFalse(MIX_SetTrackIOStream(track, ioStream, false), nameof(MIX_SetTrackIOStream));
    }

    internal override void PlayTrackCore(Pointer<MIX_Track> track)
    {
        SdlError.ThrowOnFalse(MIX_PlayTrack(track, 0), nameof(MIX_PlayTrack));
    }

    internal override void PauseTrackCore(Pointer<MIX_Track> track)
    {
        SdlError.ThrowOnFalse(MIX_PauseTrack(track), nameof(MIX_PauseTrack));
    }

    internal override void ResumeTrackCore(Pointer<MIX_Track> track)
    {
        SdlError.ThrowOnFalse(MIX_ResumeTrack(track), nameof(MIX_ResumeTrack));
    }

    internal override void StopTrackCore(Pointer<MIX_Track> track)
    {
        SdlError.ThrowOnFalse(MIX_StopTrack(track, 0), nameof(MIX_StopTrack));
    }

    internal override AudioSourceState GetTrackStateCore(Pointer<MIX_Track> track)
    {
        if (MIX_TrackPaused(track))
        {
            return AudioSourceState.Paused;
        }

        if (MIX_TrackPlaying(track))
        {
            return AudioSourceState.Playing;
        }

        return AudioSourceState.Stopped;
    }

    internal override void SetTrackGainCore(Pointer<MIX_Track> track, float gain)
    {
        SdlError.ThrowOnFalse(MIX_SetTrackGain(track, gain), nameof(MIX_SetTrackGain));
    }

    internal override void SetTrackLoopsCore(Pointer<MIX_Track> track, int loopCount)
    {
        SdlError.ThrowOnFalse(MIX_SetTrackLoops(track, loopCount), nameof(MIX_SetTrackLoops));
    }

    internal override void SetTrack3DPositionCore(Pointer<MIX_Track> track, Vector3 position)
    {
        MIX_Point3D point = new()
        {
            x = position.X,
            y = position.Y,
            z = position.Z
        };

        SdlError.ThrowOnFalse(MIX_SetTrack3DPosition(track, &point), nameof(MIX_SetTrack3DPosition));
    }

    internal override void TagTrackCore(Pointer<MIX_Track> track, string tag)
    {
        using NullTerminatedUtf8 utf8Tag = new NullTerminatedUtf8(tag);
        SdlError.ThrowOnFalse(MIX_TagTrack(track, utf8Tag.Span), nameof(MIX_TagTrack));
    }

    internal override void UntagTrackCore(Pointer<MIX_Track> track, string tag)
    {
        using NullTerminatedUtf8 utf8Tag = new NullTerminatedUtf8(tag);
        MIX_UntagTrack(track, utf8Tag.Span);
    }

    internal override void SetTagGainCore(string tag, float gain)
    {
        using NullTerminatedUtf8 utf8Tag = new NullTerminatedUtf8(tag);
        SdlError.ThrowOnFalse(MIX_SetTagGain(_mixer, utf8Tag.Span, gain), nameof(MIX_SetTagGain));
    }

    internal override void PauseTagCore(string tag)
    {
        using NullTerminatedUtf8 utf8Tag = new NullTerminatedUtf8(tag);
        SdlError.ThrowOnFalse(MIX_PauseTag(_mixer, utf8Tag.Span), nameof(MIX_PauseTag));
    }

    internal override void ResumeTagCore(string tag)
    {
        using NullTerminatedUtf8 utf8Tag = new NullTerminatedUtf8(tag);
        SdlError.ThrowOnFalse(MIX_ResumeTag(_mixer, utf8Tag.Span), nameof(MIX_ResumeTag));
    }

    internal override void StopTagCore(string tag)
    {
        using NullTerminatedUtf8 utf8Tag = new NullTerminatedUtf8(tag);
        SdlError.ThrowOnFalse(MIX_StopTag(_mixer, utf8Tag.Span, 0), nameof(MIX_StopTag));
    }

    internal override void SetMasterGainCore(float gain)
    {
        SdlError.ThrowOnFalse(MIX_SetMixerGain(_mixer, gain), nameof(MIX_SetMixerGain));
    }

    internal override void DestroyCore()
    {
        if (!_mixer.IsNull)
        {
            MIX_DestroyMixer(_mixer);
            _mixer = Pointer<MIX_Mixer>.Null;
        }

        if (_mixerInitialized)
        {
            MIX_Quit();
            _mixerInitialized = false;
        }

        if (_sdlAudioInitialized)
        {
            SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            _sdlAudioInitialized = false;
        }

        GC.KeepAlive(_sdlLifetime);
    }
}
