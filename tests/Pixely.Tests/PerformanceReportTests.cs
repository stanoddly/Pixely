using Pixely.App;
using static Pixely.Tests.FrameTimingsTests;

namespace Pixely.Tests;

public class PerformanceReportTests
{
    [Test]
    public void Update_BeforeFiveSecondsOfFrames_ReportsNothing()
    {
        RecordingLogger logger = new();
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);
        PerformanceReport report = new(timings, logger, null);

        for (int frame = 0; frame < 4; frame++)
        {
            RunFrame(timings, ref now, update: 200, waits: [500], recording: 300);
        }

        report.Update();

        Assert.That(logger.Messages, Is.Empty);
    }

    [Test]
    public void Update_AfterFiveSecondsOfFrames_ReportsTheSplitAndResetsTheTimings()
    {
        RecordingLogger logger = new();
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);
        PerformanceReport report = new(timings, logger, null);

        for (int frame = 0; frame < 5; frame++)
        {
            RunFrame(timings, ref now, update: 200, waits: [200, 300], recording: 300);
        }

        report.Update();

        Assert.Multiple(() =>
        {
            Assert.That(logger.Messages.Single(), Does.StartWith(
                "1.0 FPS over 5 frames, ms average/95th percentile/maximum: frame 1000.00/1000.00/1000.00, update 200.00/200.00/200.00, " +
                "render 300.00/300.00/300.00, swapchain wait 500.00/500.00/500.00; gen0 collections "));
            Assert.That(timings.Count, Is.Zero);
        });
    }

    [Test]
    public void Update_WithASlowFrame_ReportsThe95thPercentileAndTheMaximum()
    {
        RecordingLogger logger = new();
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);
        PerformanceReport report = new(timings, logger, null);

        for (int frame = 0; frame < 19; frame++)
        {
            RunFrame(timings, ref now, update: 200, waits: [], recording: 0);
        }

        RunFrame(timings, ref now, update: 1200, waits: [], recording: 0);
        report.Update();

        Assert.That(logger.Messages.Single(), Does.Contain(" frame 250.00/200.00/1200.00,"));
    }

    [Test]
    public void Update_WhenTheTimingsAreFull_Reports()
    {
        RecordingLogger logger = new();
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);
        PerformanceReport report = new(timings, logger, null);

        for (int frame = 0; frame < FrameTimings.Capacity; frame++)
        {
            RunFrame(timings, ref now, update: 1, waits: [], recording: 0);
        }

        report.Update();

        Assert.That(logger.Messages.Single(), Does.StartWith($"1000.0 FPS over {FrameTimings.Capacity} frames,"));
    }

    [Test]
    public void Dispose_WithFramesSinceTheLastReport_ReportsThem()
    {
        RecordingLogger logger = new();
        long now = 0;
        FrameTimings timings = new(() => now, Frequency);
        PerformanceReport report = new(timings, logger, null);
        RunFrame(timings, ref now, update: 200, waits: [500], recording: 300);
        RunFrame(timings, ref now, update: 200, waits: [500], recording: 300);

        report.Dispose();

        Assert.That(logger.Messages.Single(), Does.StartWith("1.0 FPS over 2 frames,"));
    }

    [Test]
    public void Dispose_WithoutFramesSinceTheLastReport_ReportsNothing()
    {
        RecordingLogger logger = new();
        PerformanceReport report = new(new FrameTimings(() => 0, Frequency), logger, null);

        report.Dispose();

        Assert.That(logger.Messages, Is.Empty);
    }

    [Test]
    public void Update_WritesEachSwapchainChangeOnce()
    {
        RecordingLogger logger = new();
        FrameTimings timings = new(() => 0, Frequency);
        PerformanceReport report = new(timings, logger, null);
        timings.OnSwapchainAcquired(UninitializedWindow<SwapchainWindow>(), Swapchain(1920, 1080));
        timings.OnSwapchainAcquired(UninitializedWindow<OffscreenWindow>(), Swapchain(800, 600));

        report.Update();
        report.Update();

        Assert.That(logger.Messages, Is.EqualTo(new[]
        {
            "Swapchain of view 0: 1920x1080, B8G8R8A8Unorm, present mode vsync",
            "Swapchain of view 0: 800x600, B8G8R8A8Unorm, offscreen"
        }));
    }
}
