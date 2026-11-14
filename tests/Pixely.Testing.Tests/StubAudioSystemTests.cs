using Pixely.App;
using Pixely.Audio;

namespace Pixely.Testing.Tests;

public class StubAudioSystemTests
{
    [Test]
    public void Play_PauseResumeAndStop_ChangeTheState()
    {
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
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
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);

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
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
        source.Play();
        source.Pause();

        source.Play();

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Playing));
    }

    [Test]
    public void Play_KeepsPlayingUntilFinished()
    {
        using StubAudioSystem audio = new();
        AudioSource oneShot = CreateSourceWithBuffer(audio);
        AudioSource looping = CreateSourceWithBuffer(audio);
        looping.Looping = true;
        oneShot.Play();
        looping.Play();

        audio.Finish(oneShot);
        AudioSourceState loopingWhileTheOtherFinished = looping.State;
        audio.Finish(looping);

        Assert.That((oneShot.State, loopingWhileTheOtherFinished, looping.State), Is.EqualTo((AudioSourceState.Stopped, AudioSourceState.Playing, AudioSourceState.Stopped)));
    }

    [Test]
    public void Clip_ClearedOrDisposed_StopsTheSource_AndChanged_KeepsItPlaying()
    {
        using StubAudioSystem audio = new();
        AudioSource cleared = CreateSourceWithBuffer(audio);
        AudioSource changed = CreateSourceWithBuffer(audio);
        AudioSource disposedBuffer = CreateSourceWithBuffer(audio);
        AudioStream stream = audio.OpenStream("music/theme.ogg");
        AudioSource disposedStream = audio.CreateSource();
        disposedStream.Clip = stream;
        AudioSource[] sources = [cleared, changed, disposedBuffer, disposedStream];
        foreach (AudioSource source in sources)
        {
            source.Play();
        }

        cleared.Clip = null;
        changed.Clip = audio.LoadBuffer("sounds/land.wav");
        disposedBuffer.Clip!.Dispose();
        stream.Dispose();

        Assert.That(sources.Select(source => source.State),
            Is.EqualTo(new[] { AudioSourceState.Stopped, AudioSourceState.Playing, AudioSourceState.Stopped, AudioSourceState.Stopped }));
    }

    [Test]
    public void Clip_ClearedWhilePaused_KeepsTheSourcePausedUntilItResumes()
    {
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
        source.Play();
        source.Pause();

        source.Clip = null;
        AudioSourceState cleared = source.State;
        source.Resume();

        Assert.That((cleared, source.State), Is.EqualTo((AudioSourceState.Paused, AudioSourceState.Stopped)));
    }

    [Test]
    public void Clip_ClearedAndLaterReplaced_StaysStopped()
    {
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
        source.Play();

        source.Clip = null;
        source.Clip = audio.LoadBuffer("sounds/land.wav");

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Stopped));
    }

    [Test]
    public void Clip_ReplacedByAClipThatCannotAttach_LeavesTheSourceWithoutAClipAndStopped()
    {
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
        AudioStream streamOfAnotherSource = audio.OpenStream("music/theme.ogg");
        audio.CreateSource().Clip = streamOfAnotherSource;
        source.Play();

        Assert.Multiple(() =>
        {
            Assert.That(() => source.Clip = streamOfAnotherSource, Throws.InvalidOperationException);
            Assert.That(source.Clip, Is.Null);
            Assert.That(source.State, Is.EqualTo(AudioSourceState.Stopped));
        });
    }

    [Test]
    public void LoadedPath_OfADisposedClip_Throws()
    {
        using StubAudioSystem audio = new();
        AudioBuffer buffer = audio.LoadBuffer("sounds/jump.wav");
        AudioStream stream = audio.OpenStream("music/theme.ogg");

        buffer.Dispose();
        stream.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(() => audio.LoadedPath(buffer), Throws.TypeOf<ObjectDisposedException>());
            Assert.That(() => audio.LoadedPath(stream), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void Group_PauseResumeAndStop_ApplyOnlyToItsSources()
    {
        using StubAudioSystem audio = new();
        AudioGroup group = audio.CreateGroup("music");
        AudioSource inGroup = CreateSourceWithBuffer(audio);
        inGroup.Group = group;
        AudioSource outsideGroup = CreateSourceWithBuffer(audio);
        AudioSource stoppedInGroup = CreateSourceWithBuffer(audio);
        stoppedInGroup.Group = group;
        inGroup.Play();
        outsideGroup.Play();

        group.Pause();
        (AudioSourceState, AudioSourceState) paused = (inGroup.State, outsideGroup.State);
        group.Resume();
        (AudioSourceState, AudioSourceState, AudioSourceState) resumed = (inGroup.State, outsideGroup.State, stoppedInGroup.State);
        group.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(paused, Is.EqualTo((AudioSourceState.Paused, AudioSourceState.Playing)));
            Assert.That(resumed, Is.EqualTo((AudioSourceState.Playing, AudioSourceState.Playing, AudioSourceState.Stopped)));
            Assert.That((inGroup.State, outsideGroup.State), Is.EqualTo((AudioSourceState.Stopped, AudioSourceState.Playing)));
        });
    }

    [Test]
    public void Group_LeftByASource_NoLongerAppliesToIt()
    {
        using StubAudioSystem audio = new();
        AudioGroup group = audio.CreateGroup("music");
        AudioSource source = CreateSourceWithBuffer(audio);
        source.Group = group;
        source.Play();

        source.Group = null;
        group.Pause();

        Assert.That(source.State, Is.EqualTo(AudioSourceState.Playing));
    }

    [Test]
    public void LoadedPath_OfABufferAndAStream_IsTheirPath_AndNothingIsRead()
    {
        using StubAudioSystem audio = new();

        AudioBuffer buffer = audio.LoadBuffer("sounds/jump.wav");
        AudioStream stream = audio.OpenStream("music/theme.ogg");

        Assert.That((audio.LoadedPath(buffer), audio.LoadedPath(stream)), Is.EqualTo(("sounds/jump.wav", "music/theme.ogg")));
    }

    [Test]
    public void LoadedPath_OfAnotherSystemsClip_Throws()
    {
        using StubAudioSystem audio = new();
        using StubAudioSystem other = new();

        Assert.That(() => audio.LoadedPath(other.LoadBuffer("sounds/jump.wav")), Throws.ArgumentException);
    }

    [Test]
    public void Finish_OfAnotherSystemsSourceOrADisposedOne_Throws()
    {
        using StubAudioSystem audio = new();
        using StubAudioSystem other = new();
        AudioSource disposed = audio.CreateSource();
        disposed.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(() => audio.Finish(other.CreateSource()), Throws.ArgumentException);
            Assert.That(() => audio.Finish(disposed), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void Checks_OfTheAudioSystem_StillApply()
    {
        using StubAudioSystem audio = new();
        using StubAudioSystem other = new();
        AudioSource source = audio.CreateSource();

        Assert.Multiple(() =>
        {
            Assert.That(source.Play, Throws.InvalidOperationException);
            Assert.That(() => source.Gain = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => audio.MasterGain = -1, Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => source.Clip = other.LoadBuffer("sounds/jump.wav"), Throws.ArgumentException);
            Assert.That(() => audio.CreateGroup(" "), Throws.ArgumentException);
        });
    }

    [Test]
    public void Settings_ReadBack()
    {
        using StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
        AudioGroup group = audio.CreateGroup("effects");

        source.Gain = 0.5f;
        source.Looping = true;
        source.Position = new System.Numerics.Vector3(1, 2, 3);
        source.Group = group;
        group.Gain = 0.25f;
        audio.Listener.Position = new System.Numerics.Vector3(4, 5, 6);
        audio.Listener.Gain = 0.75f;

        Assert.Multiple(() =>
        {
            Assert.That((source.Gain, source.Looping, source.Position, source.Group), Is.EqualTo((0.5f, true, new System.Numerics.Vector3(1, 2, 3), group)));
            Assert.That((group.Gain, audio.Listener.Position, audio.Listener.Gain, audio.MasterGain), Is.EqualTo((0.25f, new System.Numerics.Vector3(4, 5, 6), 0.75f, 0.75f)));
        });
    }

    [Test]
    public void Dispose_ReleasesEverything()
    {
        StubAudioSystem audio = new();
        AudioSource source = CreateSourceWithBuffer(audio);
        source.Play();

        audio.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(source.Clip, Is.Null);
            Assert.That(() => source.State, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(audio.CreateSource, Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void TestApp_WithTheGamesAudio_UsesTheStub()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.RegisterAudio();
        using IPixelyApp app = builder.Build();

        Assert.That(app.GetRequiredService<IAudioSystem>(), Is.SameAs(app.GetRequiredService<StubAudioSystem>()));
    }

    private static AudioSource CreateSourceWithBuffer(StubAudioSystem audio)
    {
        AudioSource source = audio.CreateSource();
        source.Clip = audio.LoadBuffer("sounds/jump.wav");
        return source;
    }
}
