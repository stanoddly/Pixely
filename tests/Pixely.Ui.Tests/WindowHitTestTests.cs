namespace Pixely.Ui.Tests;

/// <summary>
/// A window's hit test asks the root about a point in window coordinates, so the answer has to go through the same conversion
/// as a press: the window's size against the target's, and then the scale.
/// </summary>
public class WindowHitTestTests
{
    // A 40 by 20 target at the logical origin.
    [TestCase(10, 10, true)]
    [TestCase(39, 19, true)]
    [TestCase(40, 10, false)]
    [TestCase(10, 20, false)]
    public void AtScaleOne_TheTargetIsHitAndEmptySpaceIsNot(int x, int y, bool expected)
    {
        UiRoot root = Rooted(new RecordingPointerTarget { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) }, 1f, new Vector2Int(320, 240));

        Assert.That(root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(x, y)), Is.EqualTo(expected));
    }

    // At 2x the target covers window pixels 0 to 79 across and 0 to 39 down.
    [TestCase(79, 39, true)]
    [TestCase(80, 10, false)]
    [TestCase(10, 40, false)]
    public void AboveScaleOne_TheTargetCoversScaledWindowPixels(int x, int y, bool expected)
    {
        UiRoot root = Rooted(new RecordingPointerTarget { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) }, 2f, new Vector2Int(640, 480));

        Assert.That(root.TakesPointerAt(new Size<uint>(640, 480), new Vector2Int(x, y)), Is.EqualTo(expected));
    }

    // A window half the size of its target, as on a 200 % display with high pixel density: window pixel 19 is target pixel 38.
    [TestCase(19, 9, true)]
    [TestCase(20, 9, false)]
    public void AWindowSmallerThanItsTarget_ScalesThePositionToTheTarget(int x, int y, bool expected)
    {
        UiRoot root = Rooted(new RecordingPointerTarget { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) }, 1f, new Vector2Int(640, 480));

        Assert.That(root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(x, y)), Is.EqualTo(expected));
    }

    [Test]
    public void ADisabledTarget_IsNotHit()
    {
        // The router skips it, so a press there would reach nothing in the UI.
        UiRoot root = Rooted(new RecordingPointerTarget { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20), IsEnabled = false }, 1f, new Vector2Int(320, 240));

        Assert.That(root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(10, 10)), Is.False);
    }

    [Test]
    public void AnElementThatIsNotAPointerTarget_IsNotHit()
    {
        UiRoot root = Rooted(new Element { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) }, 1f, new Vector2Int(320, 240));

        Assert.That(root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(10, 10)), Is.False);
    }

    [Test]
    public void BeforeTheFirstUpdate_NothingIsHit()
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { new RecordingPointerTarget { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) } } });
        root.SetTargetSize(new Vector2Int(320, 240));

        Assert.That(root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(10, 10)), Is.False);
    }

    [Test]
    public void APressDraggedOntoEmptySpace_StillTakesThePointerUntilItsRelease()
    {
        // macOS hit tests every move of a drag and drops the release on a draggable point, so the capture would never end.
        UiRoot root = Rooted(new RecordingPointerTarget { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) }, 1f, new Vector2Int(320, 240));
        root.PointerPressed(new Vector2Int(10, 10));
        root.PointerMoved(new Vector2Int(100, 100));

        bool whileHeld = root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(100, 100));
        root.PointerReleased(new Vector2Int(100, 100));
        bool afterRelease = root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(100, 100));

        Assert.Multiple(() =>
        {
            Assert.That(whileHeld, Is.True);
            Assert.That(afterRelease, Is.False);
        });
    }

    [Test]
    public void AskingRoutesNothing()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(target, 1f, new Vector2Int(320, 240));

        root.TakesPointerAt(new Size<uint>(320, 240), new Vector2Int(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(target.Calls, Is.Empty);
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(0, 0)));
        });
    }

    private static UiRoot Rooted(Element content, float scale, Vector2Int targetSize)
    {
        UiRoot root = new() { Scale = scale };
        root.AddLayer(new Column { Children = { content } });
        root.SetTargetSize(targetSize);
        root.Update();
        return root;
    }
}
