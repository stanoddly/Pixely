using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Pixely.Gpu;
using SDL;

namespace Pixely.App;

// Writes FrameTimings to the log: the GPU device at startup, each swapchain change, and the frames of every period. Created
// only while PixelyConfig.EnableDiagnostics is on. See docs/diagnostics.md for what the numbers mean.
internal sealed class PerformanceReport : IUpdatable, IDisposable
{
    internal const string LoggerCategoryName = "Pixely.Diagnostics";

    private const double PeriodSeconds = 5;

    private readonly FrameTimings _timings;
    private readonly ILogger? _logger;
    private readonly GpuDevice? _gpuDevice;
    private readonly double[] _sortedTimes = new double[FrameTimings.Capacity];
    private bool _hasGen0Baseline;
    private int _periodStartGen0Collections;

    internal PerformanceReport(FrameTimings timings, ILogger? logger, GpuDevice? gpuDevice)
    {
        _timings = timings;
        _logger = logger;
        _gpuDevice = gpuDevice;
        if (gpuDevice != null)
        {
            Write(DescribeDevice(gpuDevice));
        }
    }

    public int UpdateOrder => UpdateOrders.Diagnostics;

    public void Update()
    {
        // Taken on the first frame rather than at construction, so the collections of building the app and loading its content
        // are not counted against the first period.
        if (!_hasGen0Baseline)
        {
            _periodStartGen0Collections = GC.CollectionCount(0);
            _hasGen0Baseline = true;
        }

        WriteSwapchainChanges();
        if (_timings.Duration >= PeriodSeconds || _timings.IsFull)
        {
            WritePeriod();
        }
    }

    // Reports the frames since the last report, so a run shorter than a period still reports. Build() registers this last, so
    // the root provider disposes it before its own services, among them the logger factory and the GPU device. Stages are
    // disposed before it, so their GPU memory is no longer counted.
    public void Dispose()
    {
        WriteSwapchainChanges();
        if (_timings.Count > 0)
        {
            WritePeriod();
        }
    }

    private void WriteSwapchainChanges()
    {
        IReadOnlyList<SwapchainState> changes = _timings.SwapchainChanges;
        for (int index = 0; index < changes.Count; index++)
        {
            SwapchainState swapchain = changes[index];
            // Pixely never changes SDL's swapchain parameters, so the present mode is SDL's default. An offscreen window presents
            // nothing.
            string presentation = swapchain.Offscreen ? "offscreen" : "present mode vsync";
            Write($"Swapchain of view {swapchain.ViewScope.Value}: {swapchain.Size.Width}x{swapchain.Size.Height}, {swapchain.Format}, {presentation}");
        }

        _timings.ClearSwapchainChanges();
    }

    private void WritePeriod()
    {
        int gen0Collections = GC.CollectionCount(0) - _periodStartGen0Collections;
        StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"{_timings.Count / _timings.Duration:F1} FPS over {_timings.Count} frames, ms average/95th percentile/maximum:");
        AppendTimes(builder, " frame ", _timings.FrameTimes);
        AppendTimes(builder, ", update ", _timings.UpdateTimes);
        AppendTimes(builder, ", render ", _timings.RenderTimes);
        AppendTimes(builder, ", swapchain wait ", _timings.SwapchainWaitTimes);
        builder.Append(CultureInfo.InvariantCulture, $"; gen0 collections {(double)gen0Collections / _timings.Count:F2} per frame");
        if (_gpuDevice != null)
        {
            GpuMemoryStats memory = _gpuDevice.MemoryStats;
            builder.Append($"; GPU memory {FormatBytes(memory.TotalBytes)}, textures {FormatBytes(memory.TextureBytes)}");
        }

        Write(builder.ToString());
        _timings.Reset();
        _periodStartGen0Collections = GC.CollectionCount(0);
    }

    private void AppendTimes(StringBuilder builder, string name, ReadOnlySpan<double> times)
    {
        Span<double> sorted = _sortedTimes.AsSpan(0, times.Length);
        times.CopyTo(sorted);
        sorted.Sort();
        double total = 0;
        foreach (double time in sorted)
        {
            total += time;
        }

        // Nearest rank: the smallest time that at least 95% of the times do not exceed.
        double percentile95 = sorted[(int)Math.Ceiling(0.95 * sorted.Length) - 1];
        builder.Append(name);
        builder.Append(CultureInfo.InvariantCulture, $"{total / sorted.Length * 1000:F2}/{percentile95 * 1000:F2}/{sorted[^1] * 1000:F2}");
    }

    private static string DescribeDevice(GpuDevice gpuDevice)
    {
        SDL_PropertiesID properties;
        unsafe
        {
            properties = SDL3.SDL_GetGPUDeviceProperties(gpuDevice.SdlGpuDevice);
        }

        string name = SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_NAME_STRING, "unknown") ?? "unknown";
        string driverName = SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_DRIVER_NAME_STRING, "unknown") ?? "unknown";
        string driverVersion = SDL3.SDL_GetStringProperty(properties, SDL3.SDL_PROP_GPU_DEVICE_DRIVER_VERSION_STRING, "unknown") ?? "unknown";
        return $"GPU device: {name}, backend {gpuDevice.Driver}, driver {driverName} {driverVersion}";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"),
            >= 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):F2} MB"),
            >= 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:F2} KB"),
            _ => $"{bytes} B"
        };
    }

    private void Write(string message)
    {
        if (_logger != null)
        {
            _logger.LogInformation("{Diagnostics}", message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }
}
