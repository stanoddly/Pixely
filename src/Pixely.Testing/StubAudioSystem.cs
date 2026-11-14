using System.Numerics;
using Pixely.Audio;
using Pixely.Utilities;
using XdlSharp;

namespace Pixely.Testing;

/// <summary>
/// An <see cref="AudioSystem"/> that plays nothing and reads no file. Each source keeps the state SDL_mixer would give it:
/// <c>Play</c> plays it from any state, <c>Pause</c> pauses only a playing source, <c>Resume</c> resumes only a paused one and
/// <c>Stop</c> stops it, and a group's <c>Pause</c>, <c>Resume</c> and <c>Stop</c> do the same to each of its sources. A sound has
/// no length, so it plays until the game stops it, its clip is cleared or disposed, or the test calls <see cref="Finish"/>. A new
/// clip keeps the source's state, and a paused source without a clip stops when it resumes. What the game set reads back through
/// the sources, groups and listener, and <see cref="LoadedPath"/> tells which sound a clip is.
/// </summary>
public sealed class StubAudioSystem : AudioSystem
{
    private readonly Lock _lock = new();
    private readonly Dictionary<nint, StubTrack> _tracks = new();
    private readonly Dictionary<nint, string> _audioPaths = new();
    private readonly Dictionary<nint, string> _streamPaths = new();
    private long _lastHandle;

    /// <summary>Stops the source as if its sound had reached its end, looping or not.</summary>
    public void Finish(AudioSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfNotOwn(source.AudioSystem, nameof(source));
        source.ThrowIfDisposed();
        lock (_lock)
        {
            _tracks[(nint)source.SdlTrack].State = AudioSourceState.Stopped;
        }
    }

    /// <summary>The path the buffer was loaded from, or the stream opened from.</summary>
    public string LoadedPath(IAudioClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip is not AudioClip audioClip)
        {
            throw new ArgumentException("Audio clip must be created by this audio system.", nameof(clip));
        }

        ThrowIfNotOwn(audioClip.AudioSystem, nameof(clip));
        lock (_lock)
        {
            return clip switch
            {
                AudioBuffer buffer => _audioPaths[(nint)buffer.SdlAudio],
                AudioStream stream => StreamPath(stream),
                _ => throw new ArgumentException("Audio clip must be created by this audio system.", nameof(clip))
            };
        }
    }

    internal override Pointer<MIX_Audio> LoadAudioCore(ReadOnlySpan<char> path)
    {
        Pointer<MIX_Audio> audio = NextHandle<MIX_Audio>();
        lock (_lock)
        {
            _audioPaths[(nint)audio] = path.ToString();
        }

        return audio;
    }

    internal override void DestroyAudioCore(Pointer<MIX_Audio> audio)
    {
    }

    internal override nint OpenStreamCore(ReadOnlySpan<char> path)
    {
        nint stream = (nint)NextHandle<byte>();
        lock (_lock)
        {
            _streamPaths[stream] = path.ToString();
        }

        return stream;
    }

    internal override void ReleaseStreamCore(nint stream)
    {
    }

    internal override Pointer<MIX_Track> CreateTrackCore()
    {
        Pointer<MIX_Track> track = NextHandle<MIX_Track>();
        lock (_lock)
        {
            _tracks.Add((nint)track, new StubTrack());
        }

        return track;
    }

    internal override void DestroyTrackCore(Pointer<MIX_Track> track)
    {
        lock (_lock)
        {
            _tracks.Remove((nint)track);
        }
    }

    // As in SDL_mixer, a new clip keeps the track's state. A playing track without a clip stops, as SDL_mixer's mixer stops it once it
    // finds nothing to play, and a paused one stops when it resumes.
    internal override void SetTrackAudioCore(Pointer<MIX_Track> track, Pointer<MIX_Audio> audio) => SetHasClip(track, !audio.IsNull);

    internal override void SetTrackStreamCore(Pointer<MIX_Track> track, nint stream) => SetHasClip(track, stream != 0);

    internal override void PlayTrackCore(Pointer<MIX_Track> track) => SetState(track, static _ => AudioSourceState.Playing);

    internal override void PauseTrackCore(Pointer<MIX_Track> track) => SetState(track, Pause);

    internal override void ResumeTrackCore(Pointer<MIX_Track> track) => SetState(track, Resume);

    internal override void StopTrackCore(Pointer<MIX_Track> track) => SetState(track, static _ => AudioSourceState.Stopped);

    internal override AudioSourceState GetTrackStateCore(Pointer<MIX_Track> track)
    {
        lock (_lock)
        {
            return _tracks[(nint)track].State;
        }
    }

    internal override void SetTrackGainCore(Pointer<MIX_Track> track, float gain)
    {
    }

    internal override void SetTrackLoopsCore(Pointer<MIX_Track> track, int loopCount)
    {
    }

    internal override void SetTrack3DPositionCore(Pointer<MIX_Track> track, Vector3 position)
    {
    }

    internal override void TagTrackCore(Pointer<MIX_Track> track, string tag)
    {
        lock (_lock)
        {
            _tracks[(nint)track].Tags.Add(tag);
        }
    }

    internal override void UntagTrackCore(Pointer<MIX_Track> track, string tag)
    {
        lock (_lock)
        {
            _tracks[(nint)track].Tags.Remove(tag);
        }
    }

    internal override void SetTagGainCore(string tag, float gain)
    {
    }

    internal override void PauseTagCore(string tag) => SetTaggedState(tag, Pause);

    internal override void ResumeTagCore(string tag) => SetTaggedState(tag, Resume);

    internal override void StopTagCore(string tag) => SetTaggedState(tag, static _ => AudioSourceState.Stopped);

    internal override void SetMasterGainCore(float gain)
    {
    }

    internal override void DestroyCore()
    {
    }

    private string StreamPath(AudioStream stream)
    {
        stream.ThrowIfDisposed();
        return _streamPaths[stream.Handle];
    }

    private static AudioSourceState Pause(AudioSourceState state) => state == AudioSourceState.Playing ? AudioSourceState.Paused : state;

    private static AudioSourceState Resume(AudioSourceState state) => state == AudioSourceState.Paused ? AudioSourceState.Playing : state;

    private void SetState(Pointer<MIX_Track> track, Func<AudioSourceState, AudioSourceState> change)
    {
        lock (_lock)
        {
            StubTrack stubTrack = _tracks[(nint)track];
            stubTrack.State = change(stubTrack.State);
            stubTrack.State = StopWithoutClip(stubTrack);
        }
    }

    private void SetHasClip(Pointer<MIX_Track> track, bool hasClip)
    {
        lock (_lock)
        {
            StubTrack stubTrack = _tracks[(nint)track];
            stubTrack.HasClip = hasClip;
            stubTrack.State = StopWithoutClip(stubTrack);
        }
    }

    private static AudioSourceState StopWithoutClip(StubTrack track) => !track.HasClip && track.State == AudioSourceState.Playing ? AudioSourceState.Stopped : track.State;

    private void SetTaggedState(string tag, Func<AudioSourceState, AudioSourceState> change)
    {
        lock (_lock)
        {
            foreach (StubTrack track in _tracks.Values)
            {
                if (track.Tags.Contains(tag))
                {
                    track.State = change(track.State);
                    track.State = StopWithoutClip(track);
                }
            }
        }
    }

    private void ThrowIfNotOwn(AudioSystem audioSystem, string parameterName)
    {
        if (!ReferenceEquals(audioSystem, this))
        {
            throw new ArgumentException("The audio object must be created by this audio system.", parameterName);
        }
    }

    private Pointer<T> NextHandle<T>() where T : unmanaged
    {
        unsafe
        {
            return (T*)(nint)Interlocked.Increment(ref _lastHandle);
        }
    }

    private sealed class StubTrack
    {
        public AudioSourceState State { get; set; } = AudioSourceState.Stopped;

        public bool HasClip { get; set; }

        public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
    }
}
