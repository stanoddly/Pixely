using System.Diagnostics;
using Pixely.App;
using Pixely.Audio;
using Pixely.Gpu;
using Pixely.Headless;
using Pixely.Input;
using Pixely.RenderOrchestration;
using Pixely.Testing;
using SDL;

namespace Pixely.Tests;

// In Pixely.Tests, which sees the app's config and services that are internal. Builds real apps, which start SDL.
[NonParallelizable]
public class TestAppTests
{
    [Test]
    public void CreateBuilder_RegistersTheStubAsTheDeviceWithEveryGpuService()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();

        Assert.Multiple(() =>
        {
            Assert.That(builder.IsRegistered<StubGpuDevice>(), Is.True);
            Assert.That(builder.IsRegistered<GpuDevice>(), Is.True);
            Assert.That(builder.IsRegistered<GpuMemorySystem>(), Is.True);
            Assert.That(builder.IsRegistered<GraphicsPipelineBuilder>(), Is.True);
        });
    }

    [Test]
    public void Build_WithTheGamesRendering_KeepsTheStubAndTheTestEnvironment()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering();

        using IPixelyApp app = builder.Build();

        PixelyConfig config = app.GetRequiredService<PixelyConfig>();
        Assert.Multiple(() =>
        {
            Assert.That(app.GetRequiredService<GpuDevice>(), Is.SameAs(app.GetRequiredService<StubGpuDevice>()));
            Assert.That(app.GetRequiredService<Window>(), Is.InstanceOf<StubWindow>());
            Assert.That(app.GetRequiredService<PixelyFrameClock>(), Is.TypeOf<FixedStepFrameClock>());
            Assert.That(app.ServiceProvider.GetService<TestInput>(), Is.Not.Null);
            Assert.That(config.IsIsolated, Is.True);
        });
    }

    [Test]
    public void Build_WithTheGamesOwnConfig_IsolatesItWithoutACommandConsole()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.AddSingleton(new PixelyConfig(EnableCommandConsole: true));

        using IPixelyApp app = builder.Build();

        PixelyConfig config = app.GetRequiredService<PixelyConfig>();
        Assert.Multiple(() =>
        {
            Assert.That(config.IsIsolated, Is.True);
            Assert.That(config.EnableCommandConsole, Is.False);
            Assert.That(app.ServiceProvider.GetService<AutomationCommandConsole>(), Is.Null);
        });
    }

    [Test]
    public void Build_WithHeadlessModeOn_AddsNoHeadlessMode()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.AddHeadlessSupport(name => name == HeadlessExtensions.HeadlessVariable ? "1" : null);
        builder.UseDefaultRendering();

        using IPixelyApp app = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.ServiceProvider.GetService<InputAutomationConsole>(), Is.Null);
            Assert.That(app.ServiceProvider.GetServices<IWindowFactory>(), Has.Count.EqualTo(1));
            Assert.That(app.GetRequiredService<Window>(), Is.InstanceOf<StubWindow>());
        });
    }

    [Test]
    public void Build_PicksTheDummyVideoAndAudioDriversOnlyWhileTheAppRuns()
    {
        string? videoEnvironment = Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER");
        string? audioEnvironment = Environment.GetEnvironmentVariable("SDL_AUDIO_DRIVER");
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.RegisterAudio();

        string? video;
        string? audio;
        using (IPixelyApp app = builder.Build())
        {
            video = SDL3.SDL_GetHint(SDL3.SDL_HINT_VIDEO_DRIVER);
            audio = SDL3.SDL_GetCurrentAudioDriver();
        }

        Assert.Multiple(() =>
        {
            Assert.That(video, Is.EqualTo(videoEnvironment ?? "dummy"));
            Assert.That(audio, Is.EqualTo(audioEnvironment ?? "dummy"));
            Assert.That(SDL3.SDL_GetHint(SDL3.SDL_HINT_AUDIO_DRIVER), Is.EqualTo(audioEnvironment));
        });
    }

    [Test]
    public void RunFrame_AdvancesGameTimeByAFixedStep()
    {
        using IPixelyApp app = TestApp.CreateBuilder().Build();
        FrameClock clock = app.GetRequiredService<FrameClock>();

        app.RunFrame();
        app.RunFrame();
        app.RunFrame();

        Assert.That(clock.ElapsedNanoseconds, Is.EqualTo(2 * FixedStepFrameClock.StepNanoseconds));
    }

    [Test]
    public void RunFrame_WithAHiddenWindow_DoesNotWaitForTheDisplay()
    {
        const int Frames = 30;
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering(new WindowConfig(InitiallyVisible: false));
        using IPixelyApp app = builder.Build();
        app.RunFrame();

        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < Frames; i++)
        {
            app.RunFrame();
        }

        // A frame that waited would take 16 ms; half of that for all of them leaves room for a slow machine.
        Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(Frames * 16 / 2));
    }
}
