using System.Diagnostics;

namespace Pixely;

/// <summary>
/// The clock of a headless app (<see cref="PixelyConfig.Headless"/>). Game time advances by a fixed step per frame, and the speed
/// sets only how many frames run per real second, so a faster run sees the same frame times as a slower one.
/// </summary>
internal sealed class HeadlessClock
{
    // Nobody watches these frames, so a modest rate is enough and keeps the frame loop off a full core at speed 1.
    // 1/30 s rounded up to whole nanoseconds, so 30 steps make at least a full second.
    public const ulong StepNanoseconds = 33_333_334;

    private double _speed = 1;
    private long _nextFrameTimestamp;

    // 0 runs frames as fast as they go.
    public double Speed
    {
        get => _speed;
        set
        {
            _speed = value;
            // The next frame was scheduled at the old speed; starting over applies the new one from this frame.
            _nextFrameTimestamp = 0;
        }
    }

    public void WaitForNextFrame()
    {
        if (_speed == 0)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (_nextFrameTimestamp == 0)
        {
            _nextFrameTimestamp = now;
        }

        TimeSpan remaining = Stopwatch.GetElapsedTime(now, _nextFrameTimestamp);
        if (remaining > TimeSpan.Zero)
        {
            Thread.Sleep(remaining);
            now = _nextFrameTimestamp;
        }

        // Never schedule into the past, otherwise a long frame would be followed by a burst of unpaced ones.
        long interval = (long)(StepNanoseconds / 1_000_000_000.0 / _speed * Stopwatch.Frequency);
        _nextFrameTimestamp = Math.Max(_nextFrameTimestamp, now) + interval;
    }
}
