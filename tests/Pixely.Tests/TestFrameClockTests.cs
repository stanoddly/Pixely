namespace Pixely.Tests;

public sealed class TestFrameClockTests
{
    [Test]
    public void StartTestFrame_SetsTheElapsedTimeAndCountsFrames()
    {
        TestFrameClock frameClock = new();
        List<(ulong ElapsedNanoseconds, double TimeDelta64, ulong FrameNumber)> frames = new();

        foreach (ulong elapsedMilliseconds in new ulong[] { 500, 1500, 1750 })
        {
            frameClock.StartTestFrame(elapsedMilliseconds);
            frames.Add((frameClock.ElapsedNanoseconds, frameClock.TimeDelta64, frameClock.FrameNumber));
        }

        Assert.That(frames, Is.EqualTo(new[] { (500_000_000UL, 0.5, 1UL), (1_500_000_000UL, 1.0, 2UL), (1_750_000_000UL, 0.25, 3UL) }));
        Assert.That(frameClock.ElapsedTime, Is.EqualTo(TimeSpan.FromMilliseconds(1750)));
    }
}
