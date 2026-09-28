using System.Diagnostics;

namespace Pixely.Tests;

public sealed class HeadlessClockTests
{
    [Test]
    public void WaitForNextFrame_AtSpeedZero_NeverSleeps()
    {
        HeadlessClock clock = new() { Speed = 0 };
        long start = Stopwatch.GetTimestamp();

        for (int i = 0; i < 100; i++)
        {
            clock.WaitForNextFrame();
        }

        // 100 paced frames would take over 3 seconds.
        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void Speed_Change_RestartsTheSchedule()
    {
        // 0.01 schedules the next frame over 3 seconds ahead.
        HeadlessClock clock = new() { Speed = 0.01 };
        clock.WaitForNextFrame();
        clock.Speed = 1;
        long start = Stopwatch.GetTimestamp();

        clock.WaitForNextFrame();

        Assert.That(Stopwatch.GetElapsedTime(start), Is.LessThan(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public void WaitForNextFrame_AfterALongFrame_PacesTheNextOne()
    {
        HeadlessClock clock = new() { Speed = 1 };
        clock.WaitForNextFrame();
        Thread.Sleep(TimeSpan.FromMilliseconds(200));
        // The long frame's own wait is over at once, and the frame after it waits a full step rather than catching up.
        clock.WaitForNextFrame();
        long start = Stopwatch.GetTimestamp();

        clock.WaitForNextFrame();

        Assert.That(Stopwatch.GetElapsedTime(start), Is.GreaterThan(TimeSpan.FromMilliseconds(20)));
    }
}
