namespace Pixely.Testing;

// Raises, before a frame's updates, the size changes its stub windows queued since the last frame, in the order they were queued, as
// EventService raises SDL's pixel size events.
internal sealed class StubEventSource : IEventSource
{
    private readonly FrameClock _frameClock;
    private readonly List<StubWindow> _resizedWindows = [];

    internal StubEventSource(FrameClock frameClock)
    {
        _frameClock = frameClock;
    }

    internal void QueueResize(StubWindow window)
    {
        if (!_resizedWindows.Contains(window))
        {
            _resizedWindows.Add(window);
        }
    }

    public void Process()
    {
        if (_resizedWindows.Count == 0)
        {
            return;
        }

        StubWindow[] resizedWindows = [.. _resizedWindows];
        _resizedWindows.Clear();
        ulong timestamp = _frameClock.ElapsedNanoseconds;
        foreach (StubWindow window in resizedWindows)
        {
            window.OnPixelSizeChanged(timestamp);
            window.OnScaleChanged(timestamp);
        }
    }
}
