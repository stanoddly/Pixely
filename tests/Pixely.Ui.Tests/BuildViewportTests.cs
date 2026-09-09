namespace Pixely.Ui.Tests;

/// <summary>
/// What a completed build reports about itself. A build runs application callbacks partway through,
/// and one of those can move the viewport, so what it reports has to be the viewport it actually
/// laid out for rather than whatever the root holds by the time it finishes.
/// </summary>
public class BuildViewportTests
{
    [Test]
    public void Rebuilding_RecordsTheViewportItLaidOutFor_NotOneACallbackSetAfterwards()
    {
        (UiRoot root, RecordingPointerTarget target, Element spacer) = HoveredTarget();

        // Moves the target out from under the stationary pointer, so revalidation runs the leave
        // callback in the middle of the build, and that callback resizes the viewport.
        spacer.Height = Sizing.Fixed(100);
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(target.Calls, Does.Contain("leave"), "the callback ran during the build");
            Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(320, 240)), "the build reports the viewport it actually used");
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(640, 480)));
        });
    }

    [Test]
    public void ACallbackMovingTheViewport_LeavesTheRootNeedingAnotherBuild()
    {
        (UiRoot root, RecordingPointerTarget target, Element spacer) = HoveredTarget();

        spacer.Height = Sizing.Fixed(100);
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.Update(), Is.True, "the root is still dirty");
            Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(640, 480)), "and the next build catches up");
        });
    }

    [Test]
    public void BuildVersion_RisesOnABuildAndStandsStillWhenNothingChanged()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(320, 240));
        root.AddLayer(new Element { Width = Sizing.Fixed(10), Height = Sizing.Fixed(10) });

        ulong beforeFirstBuild = root.BuildVersion;
        root.Update();
        ulong afterFirstBuild = root.BuildVersion;
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(beforeFirstBuild, Is.EqualTo(0ul), "an unbuilt root has no build to report");
            Assert.That(afterFirstBuild, Is.EqualTo(1ul));
            Assert.That(root.BuildVersion, Is.EqualTo(afterFirstBuild), "a clean root does not build again");
        });
    }

    /// <summary>
    /// A built root with the pointer parked over a target that resizes the viewport when it is told
    /// the pointer left. Growing the spacer is what pushes the target out from under the pointer.
    /// </summary>
    private static (UiRoot Root, RecordingPointerTarget Target, Element Spacer) HoveredTarget()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(320, 240));

        RecordingPointerTarget target = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50) };
        target.WhenLeft = () => root.SetViewportSize(new Vector2Int(640, 480));

        Element spacer = new() { Width = Sizing.Fixed(0), Height = Sizing.Fixed(0) };
        root.AddLayer(new Column { Children = { spacer, target } });
        root.Update();
        root.PointerMoved(new Vector2Int(10, 10));
        target.Calls.Clear();

        return (root, target, spacer);
    }
}
