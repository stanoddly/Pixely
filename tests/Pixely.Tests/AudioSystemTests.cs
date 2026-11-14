using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Pixely.App;
using Pixely.Audio;
using Pixely.Content;
using static XdlSharp.Xdl;

namespace Pixely.Tests;

// The audio system on SDL_mixer, on SDL's dummy audio driver, which plays in real time. The sound lasts several seconds, so no
// assertion depends on how far playback has advanced.
[NonParallelizable]
public sealed class AudioSystemTests
{
    private const string SoundPath = "sound.wav";

    private IPixelyApp _app = null!;
    private IAudioSystem _audioSystem = null!;

    [SetUp]
    public void SetUp()
    {
        PixelyAppBuilder builder = new();
        builder.AddSingleton(new PixelyConfig { SdlVideoDriver = "dummy", SdlAudioDriver = "dummy", IsIsolated = true });
        builder.ConfigureContent(contentSourceBuilder => contentSourceBuilder.AddSource(CreateContent()));
        builder.RegisterAudio();
        _app = builder.Build();
        _audioSystem = _app.GetRequiredService<IAudioSystem>();
        Assert.That(Marshal.PtrToStringUTF8((nint)SDL_GetCurrentAudioDriver()), Is.EqualTo("dummy"));
    }

    [TearDown]
    public void TearDown()
    {
        _app.Dispose();
    }

    [Test]
    public void Play_PauseAndResume_ChangeTheState()
    {
        AudioSource source = CreateSourceWithBuffer();
        AudioSourceState initially = source.State;

        source.Play();
        AudioSourceState playing = source.State;
        source.Pause();
        AudioSourceState paused = source.State;
        source.Resume();
        AudioSourceState resumed = source.State;
        source.Stop();

        Assert.That((initially, playing, paused, resumed, source.State),
            Is.EqualTo((AudioSourceState.Stopped, AudioSourceState.Playing, AudioSourceState.Paused, AudioSourceState.Playing, AudioSourceState.Stopped)));
    }

    [Test]
    public void PauseAndResume_InTheWrongState_ChangeNothing()
    {
        AudioSource source = CreateSourceWithBuffer();

        source.Pause();
        AudioSourceState pausedWhileStopped = source.State;
        source.Resume();
        AudioSourceState resumedWhileStopped = source.State;
        source.Play();
        source.Resume();

        Assert.That((pausedWhileStopped, resumedWhileStopped, source.State), Is.EqualTo((AudioSourceState.Stopped, AudioSourceState.Stopped, AudioSourceState.Playing)));
    }

    [Test]
    public void Play_WhilePaused_Plays()
    {
        AudioSource source = CreateSourceWithBuffer();
        source.Play();
        source.Pause();

        source.Play();

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Playing));
    }

    [Test]
    public void Play_WithoutAClip_Throws()
    {
        AudioSource source = _audioSystem.CreateSource();

        Assert.That(source.Play, Throws.InvalidOperationException);
    }

    [Test]
    public void Gains_KeepTheirValues_AndRefuseNegativeOnes()
    {
        AudioSource source = CreateSourceWithBuffer();
        AudioGroup group = _audioSystem.CreateGroup("effects");

        source.Gain = 0.5f;
        group.Gain = 0.25f;
        _audioSystem.Listener.Gain = 0.75f;

        Assert.Multiple(() =>
        {
            Assert.That((source.Gain, group.Gain, _audioSystem.Listener.Gain, _audioSystem.MasterGain), Is.EqualTo((0.5f, 0.25f, 0.75f, 0.75f)));
            Assert.That(() => source.Gain = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => group.Gain = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _audioSystem.MasterGain = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void CreateGroup_WithAName_ReturnsOneGroupPerName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_audioSystem.CreateGroup("music"), Is.SameAs(_audioSystem.CreateGroup("music")));
            Assert.That(_audioSystem.CreateGroup("music"), Is.Not.SameAs(_audioSystem.CreateGroup("ui")));
            Assert.That(() => _audioSystem.CreateGroup(" "), Throws.ArgumentException);
        });
    }

    [Test]
    public void Group_PauseResumeAndStop_ApplyOnlyToItsSources()
    {
        AudioGroup group = _audioSystem.CreateGroup("music");
        AudioSource inGroup = CreateSourceWithBuffer();
        inGroup.Group = group;
        AudioSource outsideGroup = CreateSourceWithBuffer();
        inGroup.Play();
        outsideGroup.Play();

        group.Pause();
        (AudioSourceState, AudioSourceState) paused = (inGroup.State, outsideGroup.State);
        group.Resume();
        (AudioSourceState, AudioSourceState) resumed = (inGroup.State, outsideGroup.State);
        group.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.EqualTo((AudioSourceState.Paused, AudioSourceState.Playing)));
            Assert.That(resumed, Is.EqualTo((AudioSourceState.Playing, AudioSourceState.Playing)));
            Assert.That((inGroup.State, outsideGroup.State), Is.EqualTo((AudioSourceState.Stopped, AudioSourceState.Playing)));
        });
    }

    [Test]
    public void Group_Resume_LeavesAStoppedSourceStopped()
    {
        AudioGroup group = _audioSystem.CreateGroup("music");
        AudioSource source = CreateSourceWithBuffer();
        source.Group = group;

        group.Resume();

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Stopped));
    }

    [Test]
    public void Group_LeftByASource_NoLongerAppliesToIt()
    {
        AudioGroup group = _audioSystem.CreateGroup("music");
        AudioSource source = CreateSourceWithBuffer();
        source.Group = group;
        source.Play();

        source.Group = null;
        group.Pause();

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Playing));
    }

    [Test]
    public void Stream_PlaysOnOneSourceOnly()
    {
        AudioStream stream = _audioSystem.OpenStream(SoundPath);
        AudioSource source = _audioSystem.CreateSource();
        source.Clip = stream;

        source.Play();

        Assert.Multiple(() =>
        {
            Assert.That(source.State, Is.EqualTo(AudioSourceState.Playing));
            Assert.That(() => _audioSystem.CreateSource().Clip = stream, Throws.InvalidOperationException);
        });
    }

    [Test]
    public void OpenStreamAndLoadBuffer_WithAMissingPath_Throw()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => _audioSystem.OpenStream("missing.wav"), Throws.TypeOf<FileNotFoundException>());
            Assert.That(() => _audioSystem.LoadBuffer("missing.wav"), Throws.TypeOf<FileNotFoundException>());
        });
    }

    [Test]
    public void Clip_SetToTheSameClipAgain_KeepsPlaying()
    {
        AudioBuffer buffer = _audioSystem.LoadBuffer(SoundPath);
        AudioSource source = _audioSystem.CreateSource();
        source.Clip = buffer;
        source.Play();

        source.Clip = buffer;

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Playing));
    }

    // Records SDL_mixer's behavior: a new input keeps the track's state. It cannot force the mixer to run between a detach and an
    // attach, so it does not show that the swap leaves no such moment.
    [Test]
    public void Clip_ReplacedWhilePlaying_KeepsPlaying()
    {
        AudioSource source = CreateSourceWithBuffer();
        source.Play();

        source.Clip = _audioSystem.LoadBuffer(SoundPath);
        AudioSourceState afterABuffer = source.State;
        source.Clip = _audioSystem.OpenStream(SoundPath);

        Assert.That((afterABuffer, source.State), Is.EqualTo((AudioSourceState.Playing, AudioSourceState.Playing)));
    }

    [Test]
    public void Dispose_OfABuffer_DetachesItFromItsSources()
    {
        AudioBuffer buffer = _audioSystem.LoadBuffer(SoundPath);
        AudioSource source = _audioSystem.CreateSource();
        source.Clip = buffer;

        buffer.Dispose();

        Assert.That(source.Clip, Is.Null);
    }

    [Test]
    public void Dispose_OfAStream_DetachesItFromItsSource()
    {
        AudioStream stream = _audioSystem.OpenStream(SoundPath);
        AudioSource source = _audioSystem.CreateSource();
        source.Clip = stream;

        stream.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(source.Clip, Is.Null);
            Assert.That(() => _audioSystem.CreateSource().Clip = stream, Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void Dispose_OfASource_MakesItUnusable()
    {
        AudioSource source = CreateSourceWithBuffer();

        source.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(() => source.State, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(source.Play, Throws.TypeOf<ObjectDisposedException>());
            Assert.DoesNotThrow(source.Dispose);
        });
    }

    [Test]
    public void Dispose_OfTheAudioSystem_ReleasesEverything()
    {
        AudioBuffer buffer = _audioSystem.LoadBuffer(SoundPath);
        AudioSource source = _audioSystem.CreateSource();
        source.Clip = buffer;
        source.Play();

        ((IDisposable)_audioSystem).Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(source.Clip, Is.Null);
            Assert.That(() => source.State, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(_audioSystem.CreateSource, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => _audioSystem.LoadBuffer(SoundPath), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    private AudioSource CreateSourceWithBuffer()
    {
        AudioSource source = _audioSystem.CreateSource();
        source.Clip = _audioSystem.LoadBuffer(SoundPath);
        return source;
    }

    private static DictionaryContentSource CreateContent()
    {
        FrozenDictionary<string, ImmutableArray<ContentFile>> files = new Dictionary<string, ImmutableArray<ContentFile>>
        {
            ["."] = [new ByteContentFile(SoundPath, CreateSilentWav(TimeSpan.FromSeconds(10)))]
        }.ToFrozenDictionary();
        FrozenDictionary<string, ImmutableArray<string>> directories = new Dictionary<string, ImmutableArray<string>> { ["."] = [] }.ToFrozenDictionary();
        return new DictionaryContentSource(files, directories);
    }

    // 16-bit mono PCM at 8 kHz.
    private static byte[] CreateSilentWav(TimeSpan duration)
    {
        const int SampleRate = 8000;
        int dataLength = (int)(SampleRate * duration.TotalSeconds) * sizeof(short);
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Write(new byte[dataLength]);
        writer.Flush();
        return stream.ToArray();
    }
}
