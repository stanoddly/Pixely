using Pixely.Content;
using Pixely.Utilities;
using SDL;

namespace Pixely.Audio;

public unsafe sealed class AudioSystem : IAudioSystem, IDisposable
{
    private readonly PixelyFactory _sdlLifetime;
    private readonly ContentSource _contentSource;
    private readonly LockedSet<AudioSource> _sources = new();
    private readonly LockedSet<AudioBuffer> _buffers = new();
    private readonly LockedSet<AudioStream> _streams = new();
    private readonly Dictionary<string, AudioGroup> _groups = new(StringComparer.Ordinal);
    private Pointer<MIX_Mixer> _mixer;
    private float _masterGain = 1.0f;
    private bool _sdlAudioInitialized;
    private bool _mixerInitialized;
    private bool _disposed;

    internal AudioSystem(
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

        Listener = new AudioListener(this);
    }

    public AudioListener Listener { get; }

    public float MasterGain
    {
        get
        {
            return _masterGain;
        }
        set
        {
            ThrowIfDisposed();
            ThrowIfNegative(value, nameof(value));
            SdlError.ThrowOnFalse(
                SDL3_mixer.MIX_SetMixerGain(_mixer, value),
                nameof(SDL3_mixer.MIX_SetMixerGain));
            _masterGain = value;
        }
    }

    public AudioBuffer LoadBuffer(ReadOnlySpan<char> path)
    {
        ThrowIfDisposed();

        using Stream fileStream = _contentSource.OpenStream(path);
        using MemoryStream memoryStream = new();
        fileStream.CopyTo(memoryStream);
        byte[] fileData = memoryStream.ToArray();

        fixed (byte* fileDataPointer = fileData)
        {
            Pointer<SDL_IOStream> ioStream = SDL3.SDL_IOFromConstMem((IntPtr)fileDataPointer, (UIntPtr)fileData.Length);
            SdlError.ThrowOnNull(ioStream, nameof(SDL3.SDL_IOFromConstMem));

            Pointer<MIX_Audio> sdlAudio = SDL3_mixer.MIX_LoadAudio_IO(_mixer, ioStream, true, true);
            SdlError.ThrowOnNull(sdlAudio, nameof(SDL3_mixer.MIX_LoadAudio_IO));

            AudioBuffer buffer = new(this, sdlAudio);
            _buffers.Add(buffer);
            return buffer;
        }
    }

    public AudioStream OpenStream(ReadOnlySpan<char> path)
    {
        ThrowIfDisposed();

        Stream fileStream = _contentSource.OpenStream(path);

        try
        {
            AudioStream stream = new(this, fileStream);
            _streams.Add(stream);
            return stream;
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    public AudioSource CreateSource()
    {
        ThrowIfDisposed();

        Pointer<MIX_Track> track = SDL3_mixer.MIX_CreateTrack(_mixer);
        SdlError.ThrowOnNull(track, nameof(SDL3_mixer.MIX_CreateTrack));

        AudioSource source = new(this, track);
        _sources.Add(source);
        return source;
    }

    public AudioGroup CreateGroup(string name)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Audio group name cannot be empty.", nameof(name));
        }

        if (!_groups.TryGetValue(name, out AudioGroup? group))
        {
            group = new AudioGroup(this, name);
            _groups.Add(name, group);
        }

        return group;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (AudioSource source in _sources.ClearAndCopy())
        {
            ReleaseSource(source);
        }

        foreach (AudioBuffer buffer in _buffers.ClearAndCopy())
        {
            ReleaseBuffer(buffer);
        }

        foreach (AudioStream stream in _streams.ClearAndCopy())
        {
            ReleaseStream(stream);
        }

        if (!_mixer.IsNull)
        {
            SDL3_mixer.MIX_DestroyMixer(_mixer);
            _mixer = Pointer<MIX_Mixer>.Null;
        }

        if (_mixerInitialized)
        {
            SDL3_mixer.MIX_Quit();
            _mixerInitialized = false;
        }

        if (_sdlAudioInitialized)
        {
            SDL3.SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
            _sdlAudioInitialized = false;
        }

        _groups.Clear();

        GC.KeepAlive(_sdlLifetime);
    }

    internal void SetSourceGroup(AudioSource source, AudioGroup? oldGroup, AudioGroup? newGroup)
    {
        ThrowIfDisposed();
        source.ThrowIfDisposed();

        if (oldGroup != null)
        {
            SDL3_mixer.MIX_UntagTrack(source.SdlTrack, oldGroup.Name);
        }

        if (newGroup != null)
        {
            SdlError.ThrowOnFalse(
                SDL3_mixer.MIX_TagTrack(source.SdlTrack, newGroup.Name),
                nameof(SDL3_mixer.MIX_TagTrack));
        }
    }

    internal void SetGroupGain(AudioGroup group, float gain)
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            SDL3_mixer.MIX_SetTagGain(_mixer, group.Name, gain),
            nameof(SDL3_mixer.MIX_SetTagGain));
    }

    internal void PauseGroup(AudioGroup group)
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            SDL3_mixer.MIX_PauseTag(_mixer, group.Name),
            nameof(SDL3_mixer.MIX_PauseTag));
    }

    internal void ResumeGroup(AudioGroup group)
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            SDL3_mixer.MIX_ResumeTag(_mixer, group.Name),
            nameof(SDL3_mixer.MIX_ResumeTag));
    }

    internal void StopGroup(AudioGroup group)
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            SDL3_mixer.MIX_StopTag(_mixer, group.Name, 0),
            nameof(SDL3_mixer.MIX_StopTag));
    }

    internal void UpdateSourcePositions()
    {
        foreach (AudioSource source in _sources.Copy())
        {
            source.ApplyPosition();
        }
    }

    internal void ReleaseSource(AudioSource source)
    {
        _sources.Remove(source);
        Pointer<MIX_Track> pointer = source.Pointer;
        if (pointer.IsNull)
        {
            return;
        }

        source.Clip = null;
        SDL3_mixer.MIX_DestroyTrack(pointer);
        source.Pointer = Pointer<MIX_Track>.Null;
    }

    internal void ReleaseBuffer(AudioBuffer buffer)
    {
        _buffers.Remove(buffer);
        Pointer<MIX_Audio> pointer = buffer.Pointer;
        if (pointer.IsNull)
        {
            return;
        }

        buffer.DetachFromSources();
        SDL3_mixer.MIX_DestroyAudio(pointer);
        buffer.Pointer = Pointer<MIX_Audio>.Null;
    }

    internal void ReleaseStream(AudioStream stream)
    {
        _streams.Remove(stream);
        stream.Release();
    }

    internal static void ThrowIfNegative(float value, string parameterName)
    {
        if (value < 0.0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Audio gain cannot be negative.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioSystem));
        }
    }
}
