namespace Pixely.Audio;

public sealed class AudioStream : AudioClip
{
    private readonly AudioSystem _audioSystem;
    private readonly nint _handle;
    private AudioSource? _source;
    private bool _disposed;

    internal AudioStream(AudioSystem audioSystem, nint handle)
    {
        _audioSystem = audioSystem;
        _handle = handle;
    }

    internal override AudioSystem AudioSystem => _audioSystem;

    internal nint Handle => _handle;

    internal override void AttachTo(AudioSource source)
    {
        ThrowIfDisposed();
        source.ThrowIfDisposed();

        if (_source != null && !ReferenceEquals(_source, source))
        {
            throw new InvalidOperationException("Audio stream is already attached to a source.");
        }

        _audioSystem.SetTrackStreamCore(source.SdlTrack, _handle);
        _source = source;
    }

    internal override void DetachFrom(AudioSource source, bool replaced)
    {
        if (!ReferenceEquals(_source, source))
        {
            return;
        }

        if (!replaced)
        {
            _audioSystem.SetTrackStreamCore(source.SdlTrack, 0);
        }

        _source = null;
    }

    public override void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _audioSystem.ReleaseStream(this);
    }

    internal void Release()
    {
        if (_disposed)
        {
            return;
        }

        AudioSource? source = _source;
        if (source != null)
        {
            if (source.Pointer.IsNull)
            {
                _source = null;
            }
            else if (ReferenceEquals(source.Clip, this))
            {
                source.Clip = null;
            }
            else
            {
                DetachFrom(source, replaced: false);
            }
        }

        _disposed = true;
        _audioSystem.ReleaseStreamCore(_handle);
    }

    internal void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioStream));
        }
    }
}
