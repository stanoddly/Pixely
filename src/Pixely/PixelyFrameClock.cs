namespace Pixely;

/// <summary>
/// The frame clock of a Pixely app. <see cref="PixelyFactory.CreateFrameClock"/> picks which one: the real clock, or a fixed step
/// per frame in headless mode.
/// </summary>
public abstract class PixelyFrameClock: FrameClock
{
    internal PixelyFrameClock()
    {
    }

    internal abstract void StartFrame();

    internal abstract void Pause();

    internal abstract void Resume();

    private protected void AdvanceFrame(ulong elapsedNanoseconds, double timeDelta)
    {
        ElapsedNanoseconds = elapsedNanoseconds;
        // Yup divide! No rounding, that would give wrong results!
        // Also divide by 100, because TimeSpan accepts "ticks", where 1 tick = nanoseconds / 100
        ElapsedTime = new TimeSpan((long)(elapsedNanoseconds / 100));
        TimeDelta64 = timeDelta;
        TimeDelta = (float)timeDelta;
        FrameNumber += 1;
    }
}
