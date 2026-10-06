using Microsoft.Extensions.Logging;
using Pixely.App;
using Pixely.DependencyInjection;
using Pixely.Gpu;
using SDL;

namespace Pixely.Tests;

// Builds real apps, which initializes SDL video with the dummy driver.
[NonParallelizable]
public class PixelyAppDiagnosticsTests
{
    private static readonly string[] ConfigVariables =
    [
        PixelyConfigEnvironment.GpuBackendVariable,
        PixelyConfigEnvironment.HeadlessVariable,
        PixelyConfigEnvironment.SdlLoggingVariable,
        PixelyConfigEnvironment.GpuValidationVariable,
        PixelyConfigEnvironment.PreferLowPowerGpuVariable,
        PixelyConfigEnvironment.DiagnosticsVariable,
        PixelyConfigEnvironment.UploadWaitVariable
    ];

    private readonly Dictionary<string, string?> _savedVariables = new();

    // The variables override the config each test builds with, so they are cleared for the test and restored after it.
    [SetUp]
    public void ClearConfigVariables()
    {
        foreach (string variable in ConfigVariables)
        {
            _savedVariables[variable] = Environment.GetEnvironmentVariable(variable);
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [TearDown]
    public void RestoreConfigVariables()
    {
        foreach ((string variable, string? value) in _savedVariables)
        {
            Environment.SetEnvironmentVariable(variable, value);
        }
    }

    // The report resolves the logger factory and the GPU device, so registering diagnostics must not create either ahead of the
    // app's own services. The device factory returns no device, so no GPU is needed, but still records when it runs.
    [TestCase(false)]
    [TestCase(true)]
    public void Build_KeepsTheCreationOrderOfTheAppsServices(bool enableDiagnostics)
    {
        List<string> created = new();
        PixelyAppBuilder builder = CreateBuilder(new PixelyConfig(EnableDiagnostics: enableDiagnostics));
        builder.AddSingleton<AppService>(_ =>
        {
            created.Add("app service");
            return new AppService();
        });
        builder.AddSingleton<ILoggerFactory>(_ =>
        {
            created.Add("logger factory");
            return new RecordingLoggerFactory(new RecordingLogger());
        });
        builder.AddSingleton<GpuDevice>(_ =>
        {
            created.Add("GPU device");
            return null;
        });

        using IPixelyApp app = builder.Build();

        Assert.That(created, Is.EqualTo(new[] { "app service", "logger factory", "GPU device" }));
    }

    [Test]
    public void Build_WithoutDiagnostics_RecordsNothingAndReportsNothing()
    {
        using PixelyApp app = (PixelyApp)CreateBuilder(new PixelyConfig()).Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.ServiceProvider.GetRequiredService<IFrameTimingRecorder>(), Is.SameAs(NullFrameTimingRecorder.Instance));
            Assert.That(app.ServiceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>().OfType<PerformanceReport>(), Is.Empty);
        });
    }

    [Test]
    public void Build_WithDiagnostics_RecordsFrameTimingsAndUpdatesTheReport()
    {
        using PixelyApp app = (PixelyApp)CreateBuilder(new PixelyConfig(EnableDiagnostics: true)).Build();

        Assert.Multiple(() =>
        {
            Assert.That(app.ServiceProvider.GetRequiredService<IFrameTimingRecorder>(), Is.SameAs(app.ServiceProvider.GetRequiredService<FrameTimings>()));
            Assert.That(app.ServiceProvider.GetRequiredService<ServiceRegistry<IUpdatable>>().OfType<PerformanceReport>().Count(), Is.EqualTo(1));
        });
    }

    // The recording logger throws once its factory is disposed, so the final report must come before the logger factory goes.
    [Test]
    public void RunFrame_RecordsTheFrameAndDisposeReportsIt()
    {
        long now = 0;
        RecordingLogger logger = new();
        // Diagnostics stay off, so the FrameTimings that Build() registers comes back null and this one, on a fake clock, is used.
        FrameTimings timings = new(() => now, FrameTimingsTests.Frequency);
        PixelyAppBuilder builder = CreateBuilder(new PixelyConfig());
        // Only the report's category, so SDL's messages, such as those it logs on quit, cannot come after the report.
        builder.AddSingleton<ILoggerFactory>(_ => new RecordingLoggerFactory(logger, PerformanceReport.LoggerCategoryName));
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

    private sealed class AppService;

    private sealed class ClockUpdatable(Action advance) : IUpdatable
    {
        public int UpdateOrder => UpdateOrders.Default;

        public void Update()
        {
            advance();
        }
    }
}
