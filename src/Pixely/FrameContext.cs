namespace Pixely;

/// <summary>
/// The frame context of a Pixely app. <see cref="PixelyFactory.CreateFrameContext"/> picks the clock: the real one, or a fixed step
/// per frame in headless mode.
/// </summary>
public abstract class PixelyFrameContext: FrameContext
{
    internal PixelyFrameContext()
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
