using Pixely.Utilities;
using XdlSharp;
using static XdlSharp.Xdl;

namespace Pixely.Audio;

public unsafe sealed class AudioBuffer : AudioClip
{
    private readonly AudioSystem _audioSystem;
    private readonly HashSet<AudioSource> _sources = new(ReferenceEqualityComparer.Instance);
    internal Pointer<MIX_Audio> Pointer { get; set; }

    internal AudioBuffer(AudioSystem audioSystem, Pointer<MIX_Audio> sdlAudio)
    {
        _audioSystem = audioSystem;
        Pointer = sdlAudio;
    }

    internal override AudioSystem AudioSystem => _audioSystem;

    internal Pointer<MIX_Audio> SdlAudio
    {
        get
        {
            ThrowIfDisposed();
            return Pointer;
        }
    }

    internal override void AttachTo(AudioSource source)
    {
        source.ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            MIX_SetTrackAudio(source.SdlTrack, SdlAudio),
            nameof(MIX_SetTrackAudio));
        _sources.Add(source);
    }

    internal override void DetachFrom(AudioSource source)
    {
        if (!_sources.Remove(source))
        {
            return;
        }

        source.ThrowIfDisposed();
        SdlError.ThrowOnFalse(
            MIX_SetTrackAudio(source.SdlTrack, Pointer<MIX_Audio>.Null),
            nameof(MIX_SetTrackAudio));
    }

    public override void Dispose()
    {
        _audioSystem.ReleaseBuffer(this);
    }

    internal void DetachFromSources()
    {
        foreach (AudioSource source in _sources.ToArray())
        {
            if (source.Pointer.IsNull)
            {
                _sources.Remove(source);
                continue;
            }

            if (ReferenceEquals(source.Clip, this))
            {
                source.Clip = null;
            }
            else
            {
                _sources.Remove(source);
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (Pointer.IsNull)
        {
            throw new ObjectDisposedException(nameof(AudioBuffer));
        }
    }
}
