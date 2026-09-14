namespace Pixely.Ui.Tests;

/// <summary>
/// A root laid out in logical pixels over a target of window pixels. What matters is that the tree
/// never sees the scale: the viewport, the pointer and every build are in logical pixels, and the
/// scale only decides how many target pixels each of them covers.
/// </summary>
public class UiRootScaleTests
{
    [Test]
    public void TheViewport_IsTheTargetDividedByTheScale()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(1280, 800));

        root.Scale = 2f;

        Assert.Multiple(() =>
        {
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(640, 400)));
            Assert.That(root.TargetSize, Is.EqualTo(new Vector2Int(1280, 800)), "the target is what was set");
        });
    }

    [TestCase(3, 3, 2f, 2, 2)]
    [TestCase(1280, 800, 2.5f, 512, 320)]
    [TestCase(1281, 801, 2.5f, 513, 321)]
    [TestCase(0, 0, 2f, 0, 0)]
    public void ATargetTheScaleDoesNotDivide_RoundsTheViewportUp(int targetWidth, int targetHeight, float scale, int width, int height)
    {
        Assert.That(UiRoot.ToViewportSize(new Vector2Int(targetWidth, targetHeight), scale), Is.EqualTo(new Vector2Int(width, height)));
    }

    [Test]
    public void AScaleThatDividesInFloatWithARoundingError_DoesNotAddAPhantomPixel()
    {
        // 1.1f is not 1.1: dividing in float can land a rounding error above the whole number, and
        // rounding up from there would lay out a column of logical pixels off the right of the target.
        Assert.That(UiRoot.ToViewportSize(new Vector2Int(1100, 550), 1.1f), Is.EqualTo(new Vector2Int(1000, 500)));
    }

    [Test]
    public void SettingTheScale_RaisesViewportChangedStraightAway()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        List<Vector2Int> reported = new();
        root.ViewportChanged += reported.Add;

        root.Scale = 2f;

        Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(320, 240) }), "a handler anchoring to the viewport reads the size it will be laid out in");
    }

    [Test]
    public void SettingTheScale_InvalidatesTheLayersAndRebuilds()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        Element fill = new() { Width = Sizing.Grow(), Height = Sizing.Grow() };
        root.AddLayer(new Overlay { Children = { fill } });
        root.Update();

        root.Scale = 2f;

        Assert.Multiple(() =>
        {
            Assert.That(root.Update(), Is.True);
            Assert.That(fill.Bounds, Is.EqualTo(new Rectangle(0, 0, 320, 240)), "laid out in logical pixels");
            Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(320, 240)));
            Assert.That(root.PaintedTargetSize, Is.EqualTo(new Vector2Int(640, 480)));
            Assert.That(root.PaintedScale, Is.EqualTo(2f));
        });
    }

    [Test]
    public void AScaleChangeThatLeavesTheViewportAlone_StillRebuilds()
    {
        // 4 target pixels round up to 4 logical pixels at both scales, so nothing to lay out changed,
        // but the renderer presents at the painted scale and has to be handed the new one.
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(4, 4));
        root.AddLayer(new Element { Width = Sizing.Fixed(1), Height = Sizing.Fixed(1) });
        root.Update();

        root.Scale = 1.1f;

        Assert.Multiple(() =>
        {
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(4, 4)));
            Assert.That(root.Update(), Is.True);
            Assert.That(root.PaintedScale, Is.EqualTo(1.1f));
        });
    }

    [Test]
    public void SettingTheSameScale_ChangesNothing()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        root.AddLayer(new Element { Width = Sizing.Fixed(1), Height = Sizing.Fixed(1) });
        root.Update();
        List<Vector2Int> reported = new();
        root.ViewportChanged += reported.Add;

        root.Scale = 1f;

        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.Empty);
            Assert.That(root.Update(), Is.False);
        });
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(-2f)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NaN)]
    public void AScaleBelowOneOrNotFinite_IsRefused(float scale)
    {
        UiRoot root = new();

        Assert.That(() => root.Scale = scale, Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void SettingTheScaleBeforeTheTarget_StillDerivesTheViewport()
    {
        UiRoot root = new();
        root.Scale = 2f;

        root.SetViewportSize(new Vector2Int(640, 480));

        Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(320, 240)));
    }

    [Test]
    public void ACallbackSettingTheScaleMidBuild_LeavesTheBuildAtTheScaleItLaidOutFor()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50) };
        target.WhenLeft = () => root.Scale = 2f;
        Element spacer = new() { Width = Sizing.Fixed(0), Height = Sizing.Fixed(0) };
        root.AddLayer(new Column { Children = { spacer, target } });
        root.Update();
        root.PointerMoved(new Vector2Int(10, 10));

        // Pushes the target out from under the pointer, so the leave callback runs inside the build.
        spacer.Height = Sizing.Fixed(100);
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.PaintedScale, Is.EqualTo(1f), "the build reports the scale it actually used");
            Assert.That(root.PaintedViewportSize, Is.EqualTo(new Vector2Int(640, 480)));
            Assert.That(root.Update(), Is.True, "and the next one catches up");
            Assert.That(root.PaintedScale, Is.EqualTo(2f));
        });
    }

    [Test]
    public void LoweringTheScale_CarriesThePointerToTheCoarserPixelsOrigin()
    {
        // Target pixel 37 was logical pixel 18 at 2x. Where within that pixel the pointer is was
        // rounded away, so at 1x it lands on 36 until the next motion event says otherwise.
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        root.Scale = 2f;
        root.PointerMoved(new Vector2Int(18, 0));

        root.Scale = 1f;

        Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(36, 0)));
    }

    [Test]
    public void SettingTheScaleInsideAPointerCallback_ReportsThePositionOnceTheRouteHasFinished()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(40) };
        root.AddLayer(new Column { Children = { target } });
        root.Update();
        root.PointerMoved(new Vector2Int(20, 20));
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;
        int reportedInsideTheCallback = -1;
        target.WhenPressed = () =>
        {
            root.Scale = 2f;
            reportedInsideTheCallback = reported.Count;
        };

        root.PointerPressed(new Vector2Int(20, 20));

        Assert.Multiple(() =>
        {
            Assert.That(reportedInsideTheCallback, Is.Zero, "a listener routing the pointer here could take the capture the press is about to install");
            Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(10, 10) }), "reported once the route finished");
        });
    }

    [Test]
    public void SettingTheScale_CarriesTheStationaryPointerAcross()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        root.PointerMoved(new Vector2Int(100, 50));
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;

        root.Scale = 2f;

        Assert.Multiple(() =>
        {
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(50, 25)));
            Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(50, 25) }));
        });
    }

    [Test]
    public void AScaleChange_RevalidatesHoverUnderThePointerAtTheNextBuild()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(40) };
        root.AddLayer(new Column { Children = { target } });
        root.Update();

        root.PointerMoved(new Vector2Int(20, 20));
        target.Calls.Clear();

        // Target pixel 20 is logical pixel 10 once the scale doubles, which the shrunk target still
        // covers. Left at 20, the pointer would be outside it.
        target.Width = Sizing.Fixed(15);
        target.Height = Sizing.Fixed(15);
        root.Scale = 2f;
        root.Update();

        Assert.That(target.Calls, Is.Empty, "the pointer never left the target, so it is neither left nor re-entered");
    }

    [Test]
    public void AScaleChange_MovesHoverOffATargetThePointerIsNoLongerOver()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(640, 480));
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(40) };
        root.AddLayer(new Column { Children = { target } });
        root.Update();
        root.PointerMoved(new Vector2Int(20, 20));
        target.Calls.Clear();

        // Target pixel 20 is logical pixel 10 once the scale doubles, which is past the shrunk target.
        target.Width = Sizing.Fixed(8);
        target.Height = Sizing.Fixed(8);
        root.Scale = 2f;
        root.Update();

        Assert.That(target.Calls, Is.EqualTo(new[] { "leave" }));
    }
}
