using System.Diagnostics;
using Pixely.Gpu;

namespace Pixely.App;

internal readonly record struct SwapchainState(ViewScope ViewScope, ShortSize Size, TextureFormat Format, bool Offscreen);

// The time each frame spends in its parts, and each window's swapchain, recorded while PixelyConfig.EnableDiagnostics is on.
// PerformanceReport reads and resets it. Times are in seconds.
internal sealed class FrameTimings : IFrameTimingRecorder
{
    // Preallocated, so recording never allocates. A full bag drops frames until it is reset.
    internal const int Capacity = 4096;

    private readonly Func<long> _getTimestamp;
    private readonly double _secondsPerTick;
    private readonly double[] _frameTimes = new double[Capacity];
    private readonly double[] _updateTimes = new double[Capacity];
    private readonly double[] _renderTimes = new double[Capacity];
    private readonly double[] _swapchainWaitTimes = new double[Capacity];
    private readonly Dictionary<ViewScope, SwapchainState> _swapchains = new();
    private readonly List<SwapchainState> _swapchainChanges = new();
    private bool _hasPreviousFrame;
    private long _previousFrameEnd;
    private long _frameStart;
    private long _updateEnd;
    private long _renderEnd;
    private long _swapchainWaitStart;
    private long _swapchainWaitTime;
    private long _inputWaitStart;
    private long _inputWaitTime;

    internal FrameTimings()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency)
    {
    }

    internal FrameTimings(Func<long> getTimestamp, long timestampFrequency)
    {
        _getTimestamp = getTimestamp;
        _secondsPerTick = 1.0 / timestampFrequency;
    }

    // Frames recorded since the last reset.
    internal int Count { get; private set; }

    // The frame times added up since the last reset.
    internal double Duration { get; private set; }

    internal bool IsFull => Count == Capacity;

    // From the end of the previous frame to the end of this one, so it includes the wait of a frame that no window drew and,
    // in the browser, the time until the next animation frame. The input wait is left out.
    internal ReadOnlySpan<double> FrameTimes => _frameTimes.AsSpan(0, Count);

    // Without the input wait.
    internal ReadOnlySpan<double> UpdateTimes => _updateTimes.AsSpan(0, Count);

    // Without the swapchain wait.
    internal ReadOnlySpan<double> RenderTimes => _renderTimes.AsSpan(0, Count);

    // Added up over the windows of the frame.
    internal ReadOnlySpan<double> SwapchainWaitTimes => _swapchainWaitTimes.AsSpan(0, Count);

    // Each window's swapchain on its first texture and whenever its size or format changed, since the last clear.
    internal IReadOnlyList<SwapchainState> SwapchainChanges => _swapchainChanges;

    internal void Reset()
    {
        Count = 0;
        Duration = 0;
    }

    internal void ClearSwapchainChanges()
    {
        _swapchainChanges.Clear();
    }

    public void BeginFrame()
    {
        _frameStart = _getTimestamp();
        _swapchainWaitTime = 0;
        _inputWaitTime = 0;
    }

    public void EndUpdate()
    {
        _updateEnd = _getTimestamp();
    }

    public void EndRender()
    {
        _renderEnd = _getTimestamp();
    }

    public void EndFrame()
    {
        long frameEnd = _getTimestamp();
        long frameTime = frameEnd - (_hasPreviousFrame ? _previousFrameEnd : _frameStart) - _inputWaitTime;
        _hasPreviousFrame = true;
        _previousFrameEnd = frameEnd;
        if (IsFull)
        {
            return;
        }

        _frameTimes[Count] = frameTime * _secondsPerTick;
        _updateTimes[Count] = (_updateEnd - _frameStart - _inputWaitTime) * _secondsPerTick;
        _renderTimes[Count] = (_renderEnd - _updateEnd - _swapchainWaitTime) * _secondsPerTick;
        _swapchainWaitTimes[Count] = _swapchainWaitTime * _secondsPerTick;
        Duration += _frameTimes[Count];
        Count++;
    }

    public void BeginSwapchainWait()
    {
        _swapchainWaitStart = _getTimestamp();
    }

    public void EndSwapchainWait()
    {
        _swapchainWaitTime += _getTimestamp() - _swapchainWaitStart;
    }

    public void OnSwapchainAcquired(Window window, SwapchainTexture swapchainTexture)
    {
        SwapchainState swapchain = new(window.ViewScope, swapchainTexture.Size, swapchainTexture.Format, window is OffscreenWindow);
        if (_swapchains.TryGetValue(window.ViewScope, out SwapchainState recorded) && recorded == swapchain)
        {
            return;
        }

        _swapchains[window.ViewScope] = swapchain;
        _swapchainChanges.Add(swapchain);
    }

    public void BeginInputWait()
    {
        _inputWaitStart = _getTimestamp();
    }

    public void EndInputWait()
    {
        _inputWaitTime += _getTimestamp() - _inputWaitStart;
    }
}
