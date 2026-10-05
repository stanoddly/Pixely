using System.Runtime.CompilerServices;
using Pixely.App;
using Pixely.Gpu;
using Pixely.Utilities;
using SDL;

namespace Pixely.Tests;

public class PerformanceDiagnosticsTests
{
    // One timestamp tick is one millisecond.
    private const long Frequency = 1000;

    [Test]
    public void EndFrame_BeforeFiveSecondsOfFrames_ReportsNothing()
    {
        RecordingLogger logger = new();
        long now = 0;
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, Frequency);

        for (int frame = 0; frame < 4; frame++)
        {
            RunFrame(diagnostics, ref now, update: 200, waits: [500], recording: 300);
        }

        Assert.That(logger.Messages, Is.Empty);
    }

    [Test]
    public void Dispose_WithFramesSinceTheLastReport_ReportsThem()
    {
        RecordingLogger logger = new();
        long now = 0;
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, Frequency);

        RunFrame(diagnostics, ref now, update: 200, waits: [500], recording: 300);
        RunFrame(diagnostics, ref now, update: 200, waits: [500], recording: 300);
        diagnostics.Dispose();

        Assert.That(logger.Messages.Single(), Does.StartWith("1.0 FPS over 2 frames,"));
    }

    [Test]
    public void Dispose_RightAfterAReport_ReportsNothingMore()
    {
        RecordingLogger logger = new();
        long now = 0;
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, Frequency);

        for (int frame = 0; frame < 5; frame++)
        {
            RunFrame(diagnostics, ref now, update: 200, waits: [500], recording: 300);
        }

        diagnostics.Dispose();

        Assert.That(logger.Messages.Count(), Is.EqualTo(1));
    }

    [Test]
    public void EndFrame_LeavesTheInputWaitOutOfTheFrameAndTheUpdate()
    {
        RecordingLogger logger = new();
        long now = 0;
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, Frequency);

        diagnostics.BeginFrame();
        now += 100;
        diagnostics.BeginInputWait();
        now += 3000;
        diagnostics.EndInputWait();
        now += 100;
        diagnostics.EndUpdate();
        diagnostics.EndRender();
        diagnostics.EndFrame();
        diagnostics.Dispose();

        Assert.That(logger.Messages.Single(), Does.Contain(" frame 200.00/200.00/200.00, update 200.00/200.00/200.00,"));
    }

    [Test]
    [NonParallelizable]
    public void Build_WithoutDiagnostics_RegistersNone()
    {
        using PixelyApp app = BuildApp(new PixelyConfig(Headless: true));

        Assert.That(app.ServiceProvider.GetService<PerformanceDiagnostics>(), Is.Null);
    }

    [Test]
    [NonParallelizable]
    public void Build_WithDiagnostics_RegistersThem()
    {
        using PixelyApp app = BuildApp(new PixelyConfig(Headless: true, EnableDiagnostics: true));

        Assert.That(app.ServiceProvider.GetService<PerformanceDiagnostics>(), Is.Not.Null);
    }

    [Test]
    public void EndFrame_AfterFiveSecondsOfFrames_ReportsTheSplit()
    {
        RecordingLogger logger = new();
        long now = 0;
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, Frequency);

        for (int frame = 0; frame < 5; frame++)
        {
            RunFrame(diagnostics, ref now, update: 200, waits: [200, 300], recording: 300);
        }

        Assert.That(logger.Messages.Single(), Does.StartWith("1.0 FPS over 5 frames, ms average/95th percentile/maximum:"));
        Assert.That(logger.Messages.Single(), Does.Contain(
            " frame 1000.00/1000.00/1000.00, update 200.00/200.00/200.00, render 300.00/300.00/300.00, swapchain wait 500.00/500.00/500.00;"));
    }

    [Test]
    public void EndFrame_WithASlowFrame_ReportsThe95thPercentileAndTheMaximum()
    {
        RecordingLogger logger = new();
        long now = 0;
        PerformanceDiagnostics diagnostics = new(logger, null, () => now, Frequency);

        for (int frame = 0; frame < 19; frame++)
        {
            RunFrame(diagnostics, ref now, update: 200, waits: [], recording: 0);
        }

        RunFrame(diagnostics, ref now, update: 1200, waits: [], recording: 0);

        Assert.That(logger.Messages.Single(), Does.Contain(" frame 250.00/200.00/1200.00,"));
    }

    [Test]
    public void OnSwapchainAcquired_ReportsTheFirstSwapchainAndEachChange()
    {
        RecordingLogger logger = new();
        PerformanceDiagnostics diagnostics = new(logger, null, () => 0, Frequency);
        Window window = UninitializedWindow<SwapchainWindow>();

        diagnostics.OnSwapchainAcquired(window, Swapchain(1920, 1080));
        diagnostics.OnSwapchainAcquired(window, Swapchain(1920, 1080));
        diagnostics.OnSwapchainAcquired(window, Swapchain(1280, 720));

        Assert.That(logger.Messages, Is.EqualTo(new[]
        {
            "Swapchain of view 0: 1920x1080, B8G8R8A8Unorm, present mode vsync",
            "Swapchain of view 0: 1280x720, B8G8R8A8Unorm, present mode vsync"
        }));
    }

    [Test]
    public void OnSwapchainAcquired_ForAnOffscreenWindow_ReportsNoPresentMode()
    {
        RecordingLogger logger = new();
        PerformanceDiagnostics diagnostics = new(logger, null, () => 0, Frequency);

        diagnostics.OnSwapchainAcquired(UninitializedWindow<OffscreenWindow>(), Swapchain(800, 600));

        Assert.That(logger.Messages, Is.EqualTo(new[] { "Swapchain of view 0: 800x600, B8G8R8A8Unorm, offscreen" }));
    }

    // Building initializes SDL video; the dummy driver needs no display, and an environment override still wins.
    private static PixelyApp BuildApp(PixelyConfig config)
    {
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        PixelyAppBuilder builder = new();
        builder.AddSingleton(config);
        return (PixelyApp)builder.Build();
    }

    private static void RunFrame(PerformanceDiagnostics diagnostics, ref long now, long update, long[] waits, long recording)
    {
        diagnostics.BeginFrame();
        now += update;
        diagnostics.EndUpdate();
        foreach (long wait in waits)
        {
            diagnostics.BeginSwapchainWait();
            now += wait;
            diagnostics.EndSwapchainWait();
        }

        now += recording;
        diagnostics.EndRender();
        diagnostics.EndFrame();
    }

    private static SwapchainTexture Swapchain(ushort width, ushort height)
    {
        return new SwapchainTexture(Pointer<SDL_GPUTexture>.Null, new ShortSize(width, height), TextureFormat.B8G8R8A8Unorm);
    }

    // Created uninitialised, so it never reaches SDL; its view scope is the default.
    private static TWindow UninitializedWindow<TWindow>() where TWindow : Window
    {
        return (TWindow)RuntimeHelpers.GetUninitializedObject(typeof(TWindow));
    }
}
