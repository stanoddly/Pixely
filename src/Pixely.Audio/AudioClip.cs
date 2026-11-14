namespace Pixely.Audio;

public abstract class AudioClip : IAudioClip
{
    internal AudioClip()
    {
    }

    internal abstract AudioSystem AudioSystem { get; }

    internal abstract void AttachTo(AudioSource source);
    // A replaced clip keeps the track's input, which the new clip's AttachTo replaces, so a playing track has no moment without one.
    internal abstract void DetachFrom(AudioSource source, bool replaced);

    public abstract void Dispose();
}
