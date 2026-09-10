namespace Pixely.Gpu;

public class PerformanceTracker : IUpdatable, IDisposable
{
    private readonly GpuDevice _gpuDevice;
    private readonly FrameContext _frameContext;
    private long _frameCount;
    private double _totalFrameTime;
    private double _minFrameTime = double.MaxValue;
    private double _maxFrameTime;
    private GpuMemoryStats _peakMemoryStats;

    public PerformanceTracker(GpuDevice gpuDevice, FrameContext frameContext)
    {
        _gpuDevice = gpuDevice;
        _frameContext = frameContext;
    }

    public int UpdateOrder => UpdateOrders.Diagnostics;

    public void Update()
    {
        double frameTime = _frameContext.TimeDelta64;

        if (frameTime > 0)
        {
            _frameCount++;
            _totalFrameTime += frameTime;

            if (frameTime < _minFrameTime)
            {
                _minFrameTime = frameTime;
            }

            if (frameTime > _maxFrameTime)
            {
                _maxFrameTime = frameTime;
            }
        }

        GpuMemoryStats stats = _gpuDevice.MemoryStats;

        if (stats.TotalBytes > _peakMemoryStats.TotalBytes)
        {
            _peakMemoryStats = stats;
        }
    }

    public void Dispose()
    {
        PrintSummary();
    }

    private void PrintSummary()
    {
        if (_frameCount > 0)
        {
            double averageFrameTime = _totalFrameTime / _frameCount;

            Console.WriteLine($"Frames: {_frameCount}");
            Console.WriteLine($"Average Frame Time: {averageFrameTime * 1000:F2} ms ({1.0 / averageFrameTime:F1} FPS)");
            Console.WriteLine($"Min Frame Time: {_minFrameTime * 1000:F2} ms ({1.0 / _minFrameTime:F1} FPS)");
            Console.WriteLine($"Max Frame Time: {_maxFrameTime * 1000:F2} ms ({1.0 / _maxFrameTime:F1} FPS)");
        }

        GpuMemoryStats finalStats = _gpuDevice.MemoryStats;

        Console.WriteLine($"GPU Memory: {FormatMemoryStats(finalStats)}");
        Console.WriteLine($"GPU Memory Peak: {FormatMemoryStats(_peakMemoryStats)}");
    }

    private static string FormatMemoryStats(GpuMemoryStats stats)
    {
        return $"Textures: {stats.TextureCount} ({FormatBytes(stats.TextureBytes)}), " +
               $"VertexBuffers: {stats.VertexBufferCount} ({FormatBytes(stats.VertexBufferBytes)}), " +
               $"IndexBuffers: {stats.IndexBufferCount} ({FormatBytes(stats.IndexBufferBytes)}), " +
               $"StorageBuffers: {stats.StorageBufferCount} ({FormatBytes(stats.StorageBufferBytes)}), " +
               $"Total: {FormatBytes(stats.TotalBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
            >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
            >= 1024 => $"{bytes / 1024.0:F2} KB",
            _ => $"{bytes} B"
        };
    }
}
