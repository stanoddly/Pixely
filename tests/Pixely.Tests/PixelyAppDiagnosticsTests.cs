using Microsoft.Extensions.Logging;
using Pixely.App;
using SDL;

namespace Pixely.Tests;

// Builds real apps, which initializes SDL video with the dummy driver.
[NonParallelizable]
public class PixelyAppDiagnosticsTests
{
    // The logger factory stands in for the GPU device, which a test cannot create without a GPU: both are services the report
    // resolves, so registering diagnostics must not create either ahead of the app's own services.
    [TestCase(false)]
    [TestCase(true)]
    public void Build_KeepsTheCreationOrderOfTheAppsServices(bool enableDiagnostics)
    {
        List<object> created = new();
        object appService = new();
        RecordingLoggerFactory loggerFactory = new(new RecordingLogger());
        PixelyAppBuilder builder = CreateBuilder(new PixelyConfig(EnableDiagnostics: enableDiagnostics));
        builder.OnActivated((instance, _) => created.Add(instance));
        builder.AddSingleton<object>(_ => appService);
        builder.AddSingleton<ILoggerFactory>(_ => loggerFactory);

        using IPixelyApp app = builder.Build();

        Assert.That(created.IndexOf(appService), Is.LessThan(created.IndexOf(loggerFactory)));
    }

    [Test]
    public void RunFrame_RecordsTheFrameAndDisposeReportsIt()
    {
        long now = 0;
        RecordingLogger logger = new();
        // Diagnostics stay off, so Build() registers no FrameTimings of its own and this one, on a fake clock, is used.
        FrameTimings timings = new(() => now, FrameTimingsTests.Frequency);
        PixelyAppBuilder builder = CreateBuilder(new PixelyConfig());
        builder.AddSingleton<ILoggerFactory>(_ => new RecordingLoggerFactory(logger));
        builder.AddSingleton<FrameTimings>(_ => timings);
        builder.AddSingleton<IUpdatable>(_ => new ClockUpdatable(() => now += 200));
        IPixelyApp app = builder.Build();

        app.RunFrame();
        app.RunFrame();
        app.Dispose();

        Assert.That(logger.Messages.Last(), Does.StartWith(
            "5.0 FPS over 2 frames, ms average/95th percentile/maximum: frame 200.00/200.00/200.00, update 200.00/200.00/200.00, " +
            "render 0.00/0.00/0.00, swapchain wait 0.00/0.00/0.00; gen0 collections "));
    }

    private static PixelyAppBuilder CreateBuilder(PixelyConfig config)
    {
        SDL3.SDL_SetHintWithPriority(SDL3.SDL_HINT_VIDEO_DRIVER, "dummy", SDL_HintPriority.SDL_HINT_DEFAULT);
        PixelyAppBuilder builder = new();
        builder.AddSingleton(config);
        return builder;
    }

    private sealed class ClockUpdatable(Action advance) : IUpdatable
    {
        public int UpdateOrder => UpdateOrders.Default;

        public void Update()
        {
            advance();
        }
    }
}
