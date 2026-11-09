using System.Numerics;
using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Audio;

public unsafe sealed class AudioSource : IDisposable
{
    private readonly AudioSystem _audioSystem;
    internal Pointer<MIX_Track> Pointer { get; set; }
    private AudioClip? _clip;
    private AudioGroup? _group;
    private Vector3 _position;
    private float _gain = 1.0f;
    private bool _looping;

    internal AudioSource(AudioSystem audioSystem, Pointer<MIX_Track> track)
    {
        _audioSystem = audioSystem;
        Pointer = track;
    }

    public IAudioClip? Clip
    {
        get
        {
            return _clip;
        }
        set
        {
            ThrowIfDisposed();

            if (value is not null and not AudioClip)
            {
                throw new ArgumentException("Audio clip must be created by this audio system.", nameof(value));
            }

            AudioClip? newClip = (AudioClip?)value;
            if (newClip != null && !ReferenceEquals(newClip.AudioSystem, _audioSystem))
            {
                throw new ArgumentException("Audio clip must be created by this audio system.", nameof(value));
            }

            if (ReferenceEquals(_clip, newClip))
            {
                return;
            }

            _clip?.DetachFrom(this);
            _clip = null;
            newClip?.AttachTo(this);
            _clip = newClip;
        }
    }

    public AudioGroup? Group
    {
        get
        {
            return _group;
        }
        set
        {
            ThrowIfDisposed();
            _audioSystem.SetSourceGroup(this, _group, value);
            _group = value;
        }
    }

    public float Gain
    {
        get
        {
            return _gain;
        }
        set
        {
            ThrowIfDisposed();
            AudioSystem.ThrowIfNegative(value, nameof(value));
            SdlError.ThrowOnFalse(
                MIX_SetTrackGain(SdlTrack, value),
                nameof(MIX_SetTrackGain));
            _gain = value;
        }
    }

    public bool Looping
    {
        get
        {
            return _looping;
        }
        set
        {
            ThrowIfDisposed();
            _looping = value;
            ApplyLooping();
        }
    }

    public Vector3 Position
    {
        get
        {
            return _position;
        }
        set
        {
            ThrowIfDisposed();
            _position = value;
            if (State != AudioSourceState.Stopped)
            {
                ApplyPosition();
            }
        }
    }

    public AudioSourceState State
    {
        get
        {
            ThrowIfDisposed();
            if (MIX_TrackPaused(SdlTrack))
            {
                return AudioSourceState.Paused;
            }

            if (MIX_TrackPlaying(SdlTrack))
            {
                return AudioSourceState.Playing;
            }

            return AudioSourceState.Stopped;
        }
    }

    internal Pointer<MIX_Track> SdlTrack => Pointer;

    public void Play()
    {
        ThrowIfDisposed();
        if (_clip == null)
        {
            throw new InvalidOperationException("Cannot play an audio source without a clip.");
        }

        ApplyLooping();
        ApplyPosition();
        SdlError.ThrowOnFalse(
            MIX_PlayTrack(SdlTrack, 0),
            nameof(MIX_PlayTrack));
    }

    public void Pause()
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            MIX_PauseTrack(SdlTrack),
            nameof(MIX_PauseTrack));
    }

    public void Resume()
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            MIX_ResumeTrack(SdlTrack),
            nameof(MIX_ResumeTrack));
    }

    public void Stop()
    {
        ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            MIX_StopTrack(SdlTrack, 0),
            nameof(MIX_StopTrack));
    }

    public void Dispose()
    {
        _audioSystem.ReleaseSource(this);
    }

    internal void ApplyPosition()
    {
        ThrowIfDisposed();
        Vector3 relativePosition = _position - _audioSystem.Listener.Position;
        MIX_Point3D point = new()
        {
            x = relativePosition.X,
            y = relativePosition.Y,
            z = relativePosition.Z
        };

        SdlError.ThrowOnFalse(
            MIX_SetTrack3DPosition(SdlTrack, &point),
            nameof(MIX_SetTrack3DPosition));
    }

    internal void ThrowIfDisposed()
    {
        if (Pointer.IsNull)
        {
            throw new ObjectDisposedException(nameof(AudioSource));
        }
    }

    private void ApplyLooping()
    {
        ThrowIfDisposed();
        int loopCount = _looping ? -1 : 0;
        SdlError.ThrowOnFalse(
            MIX_SetTrackLoops(SdlTrack, loopCount),
            nameof(MIX_SetTrackLoops));
    }
}
