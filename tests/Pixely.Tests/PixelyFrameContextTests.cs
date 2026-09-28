namespace Pixely.Tests;

public sealed class PixelyFrameContextTests
{
    [Test]
    public void StartFrame_Headless_StartsAtZeroAndAdvancesByTheFixedStep()
    {
        PixelyFrameContext frameContext = new(new HeadlessClock { Speed = 0 });
        List<(ulong ElapsedNanoseconds, double TimeDelta64, ulong FrameNumber)> frames = new();

        for (int i = 0; i < 3; i++)
        {
            frameContext.StartFrame();
            frames.Add((frameContext.ElapsedNanoseconds, frameContext.TimeDelta64, frameContext.FrameNumber));
        }

        double step = HeadlessClock.StepNanoseconds / 1_000_000_000.0;
        Assert.That(frames, Is.EqualTo(new[] { (0UL, 0.0, 1UL), (HeadlessClock.StepNanoseconds, step, 2UL), (2 * HeadlessClock.StepNanoseconds, step, 3UL) }));
    }

    [Test]
    public void StartFrame_Headless_ThirtyStepsMakeAtLeastOneSecond()
    {
        PixelyFrameContext frameContext = new(new HeadlessClock { Speed = 0 });

        for (int i = 0; i <= 30; i++)
        {
            frameContext.StartFrame();
        }

        Assert.That(frameContext.ElapsedTime, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
    }
}
