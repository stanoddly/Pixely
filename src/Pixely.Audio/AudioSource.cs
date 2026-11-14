using System.Numerics;
using Pixely.Utilities;
using XdlSharp;

namespace Pixely.Audio;

public sealed class AudioSource : IDisposable
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

            _clip?.DetachFrom(this, replaced: newClip != null);
            _clip = null;
            try
            {
                newClip?.AttachTo(this);
            }
            catch
            {
                // The old clip's input is still on the track, which then has no clip.
                _audioSystem.SetTrackAudioCore(SdlTrack, Pointer<MIX_Audio>.Null);
                throw;
            }

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
            _audioSystem.SetTrackGainCore(SdlTrack, value);
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
            return _audioSystem.GetTrackStateCore(SdlTrack);
        }
    }

    internal Pointer<MIX_Track> SdlTrack => Pointer;

    internal AudioSystem AudioSystem => _audioSystem;

    public void Play()
    {
        ThrowIfDisposed();
        if (_clip == null)
        {
            throw new InvalidOperationException("Cannot play an audio source without a clip.");
        }

        ApplyLooping();
        ApplyPosition();
        _audioSystem.PlayTrackCore(SdlTrack);
    }

    public void Pause()
    {
        ThrowIfDisposed();
        _audioSystem.PauseTrackCore(SdlTrack);
    }

    public void Resume()
    {
        ThrowIfDisposed();
        _audioSystem.ResumeTrackCore(SdlTrack);
    }

    public void Stop()
    {
        ThrowIfDisposed();
        _audioSystem.StopTrackCore(SdlTrack);
    }

    public void Dispose()
    {
        _audioSystem.ReleaseSource(this);
    }

    internal void ApplyPosition()
    {
        ThrowIfDisposed();
        _audioSystem.SetTrack3DPositionCore(SdlTrack, _position - _audioSystem.Listener.Position);
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
        _audioSystem.SetTrackLoopsCore(SdlTrack, _looping ? -1 : 0);
    }
}
