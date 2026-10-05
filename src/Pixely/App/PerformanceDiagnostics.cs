using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Pixely.Gpu;
using SDL;

namespace Pixely.App;

// Frame timing, the GPU device, the swapchains and GPU memory, written to the log while PixelyConfig.EnableDiagnostics is on.
// See docs/diagnostics.md for what the numbers mean.
internal sealed class PerformanceDiagnostics : IDisposable
{
    internal const string LoggerCategoryName = "Pixely.Diagnostics";

    // A period ends after this much frame time or when the samples fill, so a fast loop reports more often instead of allocating.
    private const int ReportSeconds = 5;
    private const int SampleCapacity = 4096;

    private readonly ILogger? _logger;
    private readonly GpuDevice? _gpuDevice;
    private readonly Func<long> _getTimestamp;
    private readonly long _timestampFrequency;
    private readonly long[] _frameTimes = new long[SampleCapacity];
    private readonly long[] _updateTimes = new long[SampleCapacity];
    private readonly long[] _renderTimes = new long[SampleCapacity];
    private readonly long[] _waitTimes = new long[SampleCapacity];
    private readonly Dictionary<ViewScope, (ShortSize Size, TextureFormat Format)> _swapchains = new();
    private int _sampleCount;
    private long _periodTime;
    private int _periodStartGen0Collections = GC.CollectionCount(0);
    private bool _hasPreviousFrame;
    private long _previousFrameEnd;
    private long _frameStart;
    private long _updateEnd;
    private long _renderEnd;
    private long _waitStart;
    private long _waitTime;
    private long _inputWaitStart;
    private long _inputWaitTime;

    internal PerformanceDiagnostics(ILogger? logger, GpuDevice? gpuDevice)
        : this(logger, gpuDevice, Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal PerformanceDiagnostics(ILogger? logger, GpuDevice? gpuDevice, Func<long> getTimestamp, long timestampFrequency)
    {
        _logger = logger;
        _gpuDevice = gpuDevice;
        _getTimestamp = getTimestamp;
        _timestampFrequency = timestampFrequency;
        if (gpuDevice != null)
        {
            Write(DescribeDevice(gpuDevice));
        }
    }

    internal void BeginFrame()
    {
        _frameStart = _getTimestamp();
        _waitTime = 0;
        _inputWaitTime = 0;
    }

    // Stage transitions, events and updates, without the input wait.
    internal void EndUpdate()
    {
        _updateEnd = _getTimestamp();
    }

    internal void EndRender()
    {
        _renderEnd = _getTimestamp();
    }

    internal void BeginSwapchainWait()
    {
        _waitStart = _getTimestamp();
    }

    // Accumulates, because every window of the frame waits for its own swapchain texture.
    internal void EndSwapchainWait()
    {
        _waitTime += _getTimestamp() - _waitStart;
    }

    // The time a headless app blocks on standard input for its next command, which is the operator's time, not the app's.
    internal void BeginInputWait()
    {
        _inputWaitStart = _getTimestamp();
    }

    internal void EndInputWait()
    {
        _inputWaitTime += _getTimestamp() - _inputWaitStart;
    }

    // Reports a window's swapchain on its first texture and whenever its size or format changes.
    internal void OnSwapchainAcquired(Window window, SwapchainTexture swapchainTexture)
    {
        (ShortSize Size, TextureFormat Format) swapchain = (swapchainTexture.Size, swapchainTexture.Format);
        if (_swapchains.TryGetValue(window.ViewScope, out (ShortSize Size, TextureFormat Format) reported) && reported == swapchain)
        {
            return;
        }

        _swapchains[window.ViewScope] = swapchain;
        // Pixely never changes SDL's swapchain parameters, so the present mode is SDL's default. An offscreen window presents
        // nothing.
        string presentation = window is OffscreenWindow ? "offscreen" : "present mode vsync";
        Write($"Swapchain of view {window.ViewScope.Value}: {swapchain.Size.Width}x{swapchain.Size.Height}, {swapchain.Format}, {presentation}");
    }

    // The frame time is measured from the end of the previous frame, so it includes the wait of a frame that no window drew
    // and, in the browser, the time until the next animation frame. The input wait is left out.
    internal void EndFrame()
    {
        long frameEnd = _getTimestamp();
        long frameTime = frameEnd - (_hasPreviousFrame ? _previousFrameEnd : _frameStart) - _inputWaitTime;
        _hasPreviousFrame = true;
        _previousFrameEnd = frameEnd;

        _frameTimes[_sampleCount] = frameTime;
        _updateTimes[_sampleCount] = _updateEnd - _frameStart - _inputWaitTime;
        _renderTimes[_sampleCount] = _renderEnd - _updateEnd - _waitTime;
        _waitTimes[_sampleCount] = _waitTime;
        _sampleCount++;
        _periodTime += frameTime;

        if (_periodTime >= ReportSeconds * _timestampFrequency || _sampleCount == SampleCapacity)
        {
            ReportPeriod();
        }
    }

    // Reports the frames since the last report, so a run shorter than a period still reports. The provider disposes this before
    // the logger factory and the GPU device it was created from.
    public void Dispose()
    {
        if (_sampleCount > 0)
        {
            ReportPeriod();
        }
    }

    private void ReportPeriod()
    {
        Write(DescribePeriod());
        _sampleCount = 0;
        _periodTime = 0;
        _periodStartGen0Collections = GC.CollectionCount(0);
    }

    private string DescribePeriod()
    {
        double seconds = (double)_periodTime / _timestampFrequency;
        int gen0Collections = GC.CollectionCount(0) - _periodStartGen0Collections;
        StringBuilder builder = new();
        builder.Append(CultureInfo.InvariantCulture, $"{_sampleCount / seconds:F1} FPS over {_sampleCount} frames, ms average/95th percentile/maximum:");
        AppendTimes(builder, " frame ", _frameTimes);
        AppendTimes(builder, ", update ", _updateTimes);
        AppendTimes(builder, ", render ", _renderTimes);
        AppendTimes(builder, ", swapchain wait ", _waitTimes);
        builder.Append(CultureInfo.InvariantCulture, $"; gen0 collections {(double)gen0Collections / _sampleCount:F2} per frame");
        if (_gpuDevice != null)
        {
            GpuMemoryStats memory = _gpuDevice.MemoryStats;
            builder.Append($"; GPU memory {FormatBytes(memory.TotalBytes)}, textures {FormatBytes(memory.TextureBytes)}");
        }

        return builder.ToString();
    }

    // Sorts the period's samples in place; they are discarded after the report.
    private void AppendTimes(StringBuilder builder, string name, long[] times)
    {
        Span<long> samples = times.AsSpan(0, _sampleCount);
        samples.Sort();
        long total = 0;
        foreach (long sample in samples)
        {
            total += sample;
        }

        // Nearest rank: the smallest sample that at least 95% of the samples do not exceed.
        long percentile95 = samples[(int)Math.Ceiling(0.95 * samples.Length) - 1];
        builder.Append(name);
        builder.Append(CultureInfo.InvariantCulture, $"{ToMilliseconds(total) / samples.Length:F2}/{ToMilliseconds(percentile95):F2}/{ToMilliseconds(samples[^1]):F2}");
    }

    private double ToMilliseconds(long ticks)
    {
        return ticks * 1000.0 / _timestampFrequency;
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
