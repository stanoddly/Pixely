using System.Numerics;
using Pixely.Utilities;
using XdlSharp;

namespace Pixely.Audio;

/// <summary>
/// The audio system: its checks and the lifetime of its sources, buffers, streams and groups. What plays them is up to the
/// derived system: SDL_mixer in an app, and Pixely.Testing's <c>StubAudioSystem</c> in a test app, which plays nothing.
/// </summary>
public abstract class AudioSystem : IAudioSystem, IDisposable
{
    private readonly LockedSet<AudioSource> _sources = new();
    private readonly LockedSet<AudioBuffer> _buffers = new();
    private readonly LockedSet<AudioStream> _streams = new();
    private readonly Dictionary<string, AudioGroup> _groups = new(StringComparer.Ordinal);
    private float _masterGain = 1.0f;
    private bool _disposed;

    internal AudioSystem()
    {
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
            SetMasterGainCore(value);
            _masterGain = value;
        }
    }

    public AudioBuffer LoadBuffer(ReadOnlySpan<char> path)
    {
        ThrowIfDisposed();

        AudioBuffer buffer = new(this, LoadAudioCore(path));
        _buffers.Add(buffer);
        return buffer;
    }

    public AudioStream OpenStream(ReadOnlySpan<char> path)
    {
        ThrowIfDisposed();

        AudioStream stream = new(this, OpenStreamCore(path));
        _streams.Add(stream);
        return stream;
    }

    public AudioSource CreateSource()
    {
        ThrowIfDisposed();

        AudioSource source = new(this, CreateTrackCore());
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

        DestroyCore();
        _groups.Clear();
    }

    internal void SetSourceGroup(AudioSource source, AudioGroup? oldGroup, AudioGroup? newGroup)
    {
        ThrowIfDisposed();
        source.ThrowIfDisposed();

        if (oldGroup != null)
        {
            UntagTrackCore(source.SdlTrack, oldGroup.Name);
        }

        if (newGroup != null)
        {
            TagTrackCore(source.SdlTrack, newGroup.Name);
        }
    }

    internal void SetGroupGain(AudioGroup group, float gain)
    {
        ThrowIfDisposed();
        SetTagGainCore(group.Name, gain);
    }

    internal void PauseGroup(AudioGroup group)
    {
        ThrowIfDisposed();
        PauseTagCore(group.Name);
    }

    internal void ResumeGroup(AudioGroup group)
    {
        ThrowIfDisposed();
        ResumeTagCore(group.Name);
    }

    internal void StopGroup(AudioGroup group)
    {
        ThrowIfDisposed();
        StopTagCore(group.Name);
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
        DestroyTrackCore(pointer);
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
        DestroyAudioCore(pointer);
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

    // A loaded sound, decoded in full.
    internal abstract Pointer<MIX_Audio> LoadAudioCore(ReadOnlySpan<char> path);

    internal abstract void DestroyAudioCore(Pointer<MIX_Audio> audio);

    // A sound decoded while it plays. The content opens here, so a missing path throws from OpenStream.
    internal abstract nint OpenStreamCore(ReadOnlySpan<char> path);

    internal abstract void ReleaseStreamCore(nint stream);

    internal abstract Pointer<MIX_Track> CreateTrackCore();

    internal abstract void DestroyTrackCore(Pointer<MIX_Track> track);

    // A null audio detaches the track's audio.
    internal abstract void SetTrackAudioCore(Pointer<MIX_Track> track, Pointer<MIX_Audio> audio);

    // A zero stream detaches the track's stream.
    internal abstract void SetTrackStreamCore(Pointer<MIX_Track> track, nint stream);

    internal abstract void PlayTrackCore(Pointer<MIX_Track> track);

    internal abstract void PauseTrackCore(Pointer<MIX_Track> track);

    internal abstract void ResumeTrackCore(Pointer<MIX_Track> track);

    internal abstract void StopTrackCore(Pointer<MIX_Track> track);

    internal abstract AudioSourceState GetTrackStateCore(Pointer<MIX_Track> track);

    internal abstract void SetTrackGainCore(Pointer<MIX_Track> track, float gain);

    // -1 loops forever, 0 plays once.
    internal abstract void SetTrackLoopsCore(Pointer<MIX_Track> track, int loopCount);

    // Relative to the listener.
    internal abstract void SetTrack3DPositionCore(Pointer<MIX_Track> track, Vector3 position);

    internal abstract void TagTrackCore(Pointer<MIX_Track> track, string tag);

    internal abstract void UntagTrackCore(Pointer<MIX_Track> track, string tag);

    internal abstract void SetTagGainCore(string tag, float gain);

    internal abstract void PauseTagCore(string tag);

    internal abstract void ResumeTagCore(string tag);

    internal abstract void StopTagCore(string tag);

    internal abstract void SetMasterGainCore(float gain);

    // Runs once, after every source, buffer and stream is released.
    internal abstract void DestroyCore();

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioSystem));
        }
    }
}
