using System.Numerics;
using Pixely.App;
using Pixely.Audio;
using Pixely.Input;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Audio;

static class Program
{
    private const string BeepPath = "audio/beep-example.ogg";
    private const int SourceCount = 4;
    private const float BufferedGain = 0.45f;
    private const float StreamGain = 0.15f;

    static int Main(string[] args)
    {
        PixelyAppBuilder builder = new();
        builder
            .UseDefaultContent()
            .UseDefaultRendering(
                new WindowConfig(Size: (640, 480), Title: "Audio Tutorial"))
            .RegisterAudio();

        builder.OnStart((IAudioSystem audioSystem, IKeyboardService keyboardService, AppControl appControl) =>
        {
            DefaultAudioGroups groups = DefaultAudioGroups.Create(audioSystem);
            AudioBuffer beep = audioSystem.LoadBuffer(BeepPath);
            AudioStream streamedBeep = audioSystem.OpenStream(BeepPath);
            AudioSource[] sources = CreateBufferedSources(audioSystem, groups, beep);
            AudioSource streamSource = CreateStreamSource(audioSystem, groups, streamedBeep);
            AudioGroup currentGroup = groups.Effects;
            float sourceX = 0.0f;
            int sourceIndex = 0;

            Console.WriteLine("Audio tutorial");
            Console.WriteLine("Space: play the buffered beep");
            Console.WriteLine("M: toggle a looping streamed beep");
            Console.WriteLine($"Buffered source gain: {BufferedGain:0.00}");
            Console.WriteLine($"Stream source gain: {StreamGain:0.00}");
            Console.WriteLine("Left/Right: move the source");
            Console.WriteLine("1: effects group, 2: UI group, 3: muted UI group");
            Console.WriteLine("Escape: quit");
            Console.WriteLine($"Source group: {currentGroup.Name}");
            Console.WriteLine($"Source position: {sourceX:0.0}");

            keyboardService.KeyDown += eventArgs =>
            {
                // Every action here is a discrete press; holding a key must not retrigger it.
                if (eventArgs.Repeat)
                {
                    return;
                }

                if (eventArgs.Key == VirtualKey.Space)
                {
                    AudioSource source = sources[sourceIndex];
                    source.Group = currentGroup;
                    source.Position = new Vector3(sourceX, 0.0f, 0.0f);
                    source.Play();
                    sourceIndex = (sourceIndex + 1) % sources.Length;
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.M)
                {
                    ToggleStream(streamSource);
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.Left)
                {
                    sourceX -= 2.0f;
                    Console.WriteLine($"Source position: {sourceX:0.0}");
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.Right)
                {
                    sourceX += 2.0f;
                    Console.WriteLine($"Source position: {sourceX:0.0}");
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.Number1)
                {
                    currentGroup = groups.Effects;
                    groups.Effects.Gain = 1.0f;
                    Console.WriteLine($"Source group: {currentGroup.Name}");
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.Number2)
                {
                    currentGroup = groups.Ui;
                    groups.Ui.Gain = 1.0f;
                    Console.WriteLine($"Source group: {currentGroup.Name}");
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.Number3)
                {
                    currentGroup = groups.Ui;
                    groups.Ui.Gain = 0.0f;
                    Console.WriteLine($"Source group: {currentGroup.Name} muted");
                    eventArgs.Consume();
                    return;
                }

                if (eventArgs.Key == VirtualKey.Escape)
                {
                    appControl.Quit();
                    eventArgs.Consume();
                }
            };
        });

        using IPixelyApp pixelyApp = builder.Build();
        return pixelyApp.Run();
    }

    private static AudioSource[] CreateBufferedSources(IAudioSystem audioSystem, DefaultAudioGroups groups, AudioBuffer buffer)
    {
        AudioSource[] sources = new AudioSource[SourceCount];
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = audioSystem.CreateSource();
            source.Clip = buffer;
            source.Gain = BufferedGain;
            source.Group = groups.Effects;
            sources[i] = source;
        }

        return sources;
    }

    private static AudioSource CreateStreamSource(IAudioSystem audioSystem, DefaultAudioGroups groups, AudioStream stream)
    {
        AudioSource source = audioSystem.CreateSource();
        source.Clip = stream;
        source.Gain = StreamGain;
        source.Group = groups.Music;
        source.Looping = true;
        return source;
    }

    private static void ToggleStream(AudioSource source)
    {
        switch (source.State)
        {
            case AudioSourceState.Playing:
                source.Pause();
                Console.WriteLine("Streamed audio paused");
                break;
            case AudioSourceState.Paused:
                source.Resume();
                Console.WriteLine("Streamed audio resumed");
                break;
            case AudioSourceState.Stopped:
                source.Play();
                Console.WriteLine("Streamed audio playing");
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}
