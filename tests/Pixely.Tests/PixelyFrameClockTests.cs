namespace Pixely.Tests;

public sealed class PixelyFrameClockTests
{
    [TestCase(true, typeof(FixedStepFrameClock))]
    [TestCase(false, typeof(SdlFrameClock))]
    public void CreateFrameClock_PicksTheClockByHeadlessMode(bool headless, Type expectedType)
    {
        using PixelyFactory factory = new(new PixelyConfig(Headless: headless), null);

        Assert.That(factory.CreateFrameClock(), Is.TypeOf(expectedType));
    }

    [Test]
    public void StartFrame_FixedStep_StartsAtZeroAndAdvancesByTheStep()
    {
        FixedStepFrameClock frameClock = new();
        List<(ulong ElapsedNanoseconds, double TimeDelta64, ulong FrameNumber)> frames = new();

        for (int i = 0; i < 3; i++)
        {
            frameClock.StartFrame();
            frames.Add((frameClock.ElapsedNanoseconds, frameClock.TimeDelta64, frameClock.FrameNumber));
        }

        double step = FixedStepFrameClock.StepNanoseconds / 1_000_000_000.0;
        Assert.That(frames, Is.EqualTo(new[] { (0UL, 0.0, 1UL), (FixedStepFrameClock.StepNanoseconds, step, 2UL), (2 * FixedStepFrameClock.StepNanoseconds, step, 3UL) }));
    }

    [Test]
    public void StartFrame_FixedStep_ThirtyStepsMakeAtLeastOneSecond()
    {
        FixedStepFrameClock frameClock = new();

        for (int i = 0; i <= 30; i++)
        {
            frameClock.StartFrame();
        }

        Assert.That(frameClock.ElapsedTime, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void PauseAndResume_FixedStep_ChangeNothing()
    {
        FixedStepFrameClock frameClock = new();
        frameClock.StartFrame();

        frameClock.Pause();
        frameClock.StartFrame();
        frameClock.Resume();
        frameClock.StartFrame();

        Assert.Multiple(() =>
        {
            Assert.That(frameClock.FrameNumber, Is.EqualTo(3));
            Assert.That(frameClock.ElapsedNanoseconds, Is.EqualTo(2 * FixedStepFrameClock.StepNanoseconds));
        });
    }
}
