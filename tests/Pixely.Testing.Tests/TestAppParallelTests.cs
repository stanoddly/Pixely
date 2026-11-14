using Pixely.App;
using Pixely.Audio;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Text;
using Pixely.Ui;

namespace Pixely.Testing.Tests;

public class TestAppParallelTests
{
    private const int ThreadCount = 8;
    private const int AppsPerThread = 5;

    [Test]
    public void Apps_OnSeveralThreadsAtOnce_EachSeeOnlyTheirOwnInputFramesAndSounds()
    {
        using Barrier start = new(ThreadCount);
        Task<List<string>>[] threads = Enumerable.Range(0, ThreadCount)
            .Select(index => Task.Factory.StartNew(() => RunApps(index, start), TaskCreationOptions.LongRunning))
            .ToArray();

        Task.WaitAll(threads);

        Assert.That(threads.SelectMany(thread => thread.Result), Is.Empty);
    }

    // Returns what went wrong. Each app has its own window width, and presses as many keys as that width's index says.
    private static List<string> RunApps(int index, Barrier start)
    {
        List<string> errors = [];
        start.SignalAndWait();
        for (int run = 0; run < AppsPerThread; run++)
        {
            uint width = (uint)(100 + index);
            int keyPresses = index + 1;
            PixelyAppBuilder builder = TestApp.CreateBuilder();
            builder.UseDefaultRendering(new WindowConfig(Size: (width, 50)));
            builder.UseUi();
            builder.RegisterAudio();
            builder.OnBuilt(provider =>
            {
                UiRoot root = provider.GetUiRoot();
                root.Style = new UiStyle(provider.GetRequiredService<IFontSystem>().Load("fonts/Any.ttf", 12));
                root.AddLayer(new Label($"App {index}"));
            });

            using IPixelyApp app = builder.Build();
            int pressed = 0;
            app.GetRequiredService<IKeyboardService>().KeyDown += _ => pressed++;
            TestInput input = app.GetRequiredService<TestInput>();
            for (int key = 0; key < keyPresses; key++)
            {
                input.KeyPress(Scancode.Space);
            }

            IAudioSystem audio = app.GetRequiredService<IAudioSystem>();
            AudioSource source = audio.CreateSource();
            source.Clip = audio.LoadBuffer($"sounds/{index}.wav");
            if (index % 2 == 0)
            {
                source.Play();
            }

            app.RunFrame();
            app.RunFrame();

            StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
            ShortSize[] targetSizes = device.SubmittedCommandBuffers.SelectMany(commandBuffer => commandBuffer.RenderPasses)
                .SelectMany(renderPass => renderPass.ColorTargets).Select(target => target.Size).Distinct().ToArray();
            AudioSourceState expectedState = index % 2 == 0 ? AudioSourceState.Playing : AudioSourceState.Stopped;
            if (pressed != keyPresses)
            {
                errors.Add($"App {index} saw {pressed} key presses instead of {keyPresses}.");
            }

            if (!targetSizes.SequenceEqual([new ShortSize((ushort)width, 50)]))
            {
                errors.Add($"App {index} rendered to {string.Join(", ", targetSizes)} instead of its own {width} x 50 window.");
            }

            if (source.State != expectedState || app.GetRequiredService<StubAudioSystem>().LoadedPath(source.Clip!) != $"sounds/{index}.wav")
            {
                errors.Add($"App {index}'s sound is {source.State} instead of {expectedState}.");
            }
        }

        return errors;
    }
}
