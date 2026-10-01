namespace Pixely;

// The frame clock of a headless app (PixelyConfig.Headless). Game time starts at 0 and advances by a fixed step per frame,
// so every run of a script sees the same frame times however fast its frames run.
internal sealed class FixedStepFrameClock : PixelyFrameClock
{
    // 1/30 s rounded up to whole nanoseconds, so 30 steps make at least a full second.
    public const ulong StepNanoseconds = 33_333_334;

    internal override void StartFrame()
    {
        // The first frame has no previous one to measure from, the same as with the real clock.
        if (FrameNumber == 0)
        {
            AdvanceFrame(0, 0);
            return;
        }

        AdvanceFrame(ElapsedNanoseconds + StepNanoseconds, StepNanoseconds / 1_000_000_000.0);
    }

    // No real time passes in a headless run, so there is nothing to leave out.
    internal override void Pause()
    {
    }

    internal override void Resume()
    {
    }
}
