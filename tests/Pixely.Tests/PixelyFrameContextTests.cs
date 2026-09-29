namespace Pixely.Tests;

public sealed class PixelyFrameContextTests
{
    [TestCase(true, typeof(FixedStepFrameContext))]
    [TestCase(false, typeof(SdlFrameContext))]
    public void CreateFrameContext_PicksTheClockByHeadlessMode(bool headless, Type expectedType)
    {
        using PixelyFactory factory = new(new PixelyConfig(Headless: headless), null);

        Assert.That(factory.CreateFrameContext(), Is.TypeOf(expectedType));
    }

    [Test]
    public void StartFrame_FixedStep_StartsAtZeroAndAdvancesByTheStep()
    {
        FixedStepFrameContext frameContext = new();
        List<(ulong ElapsedNanoseconds, double TimeDelta64, ulong FrameNumber)> frames = new();

        for (int i = 0; i < 3; i++)
        {
            frameContext.StartFrame();
            frames.Add((frameContext.ElapsedNanoseconds, frameContext.TimeDelta64, frameContext.FrameNumber));
        }

        double step = FixedStepFrameContext.StepNanoseconds / 1_000_000_000.0;
        Assert.That(frames, Is.EqualTo(new[] { (0UL, 0.0, 1UL), (FixedStepFrameContext.StepNanoseconds, step, 2UL), (2 * FixedStepFrameContext.StepNanoseconds, step, 3UL) }));
    }

    [Test]
    public void StartFrame_FixedStep_ThirtyStepsMakeAtLeastOneSecond()
    {
        FixedStepFrameContext frameContext = new();

        for (int i = 0; i <= 30; i++)
        {
            frameContext.StartFrame();
        }

        Assert.That(frameContext.ElapsedTime, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void PauseAndResume_FixedStep_ChangeNothing()
    {
        FixedStepFrameContext frameContext = new();
        frameContext.StartFrame();

        frameContext.Pause();
        frameContext.StartFrame();
        frameContext.Resume();
        frameContext.StartFrame();

        Assert.Multiple(() =>
        {
            Assert.That(frameContext.FrameNumber, Is.EqualTo(3));
            Assert.That(frameContext.ElapsedNanoseconds, Is.EqualTo(2 * FixedStepFrameContext.StepNanoseconds));
        });
    }
}
