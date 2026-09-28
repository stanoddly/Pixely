using SDL;

namespace Pixely;

public class PixelyFrameContext: FrameContext
{
    // 100 ms = 0.1 seconds maximum delta time
    private const double MaxDeltaTime = 0.100;
    private ulong _pausedNanoseconds;
    private ulong _pauseStartNanoseconds;
    private bool _paused;
    private readonly HeadlessClock? _headlessClock;

    internal PixelyFrameContext(HeadlessClock? headlessClock)
    {
        _headlessClock = headlessClock;
    }

    public void StartFrame()
    {
        if (_paused)
        {
            return;
        }

        if (_headlessClock != null)
        {
            StartHeadlessFrame(_headlessClock);
            return;
        }

        ulong previousElapsedNanoseconds = ElapsedNanoseconds;

        ElapsedNanoseconds = SDL3.SDL_GetTicksNS() - _pausedNanoseconds;

        // Yup divide! No rounding, that would give wrong results!
        // Also divide by 100, because TimeSpan accepts "ticks", where 1 tick = nanoseconds / 100
        ElapsedTime = new TimeSpan((long)(ElapsedNanoseconds / 100));

        // There could have been some loading, so the very first StartFrame would calculate several seconds! 😅
        if (previousElapsedNanoseconds != 0)
        {
            // Calculate the actual time delta
            double actualDelta = (ElapsedNanoseconds - previousElapsedNanoseconds) / 1_000_000_000.0;
            
            // Clamp the delta time to our maximum value
            TimeDelta64 = Math.Min(actualDelta, MaxDeltaTime);
            TimeDelta = (float)TimeDelta64;
        }

        FrameNumber += 1;
    }

    // Game time starts at 0 and advances by a fixed step, so every run of a script sees the same times whatever its speed.
    private void StartHeadlessFrame(HeadlessClock headlessClock)
    {
        headlessClock.WaitForNextFrame();

        // The first frame has no previous one to measure from, the same as with the real clock.
        if (FrameNumber != 0)
        {
            ElapsedNanoseconds += HeadlessClock.StepNanoseconds;
            TimeDelta64 = HeadlessClock.StepNanoseconds / 1_000_000_000.0;
            TimeDelta = (float)TimeDelta64;
        }

        ElapsedTime = new TimeSpan((long)(ElapsedNanoseconds / 100));
        FrameNumber += 1;
    }

    internal void Pause()
    {
        if (_paused)
        {
            throw new InvalidOperationException("Frame context is already paused.");
        }

        _pauseStartNanoseconds = SDL3.SDL_GetTicksNS();
        _paused = true;
    }

    internal void Resume()
    {
        if (!_paused)
        {
            throw new InvalidOperationException("Frame context is not paused.");
        }

        ulong pauseEndNanoseconds = SDL3.SDL_GetTicksNS();
        _pausedNanoseconds += pauseEndNanoseconds - _pauseStartNanoseconds;
        _pauseStartNanoseconds = 0;
        _paused = false;
    }
}
