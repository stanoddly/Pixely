using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// What the tree cannot work out for itself: where the pointer is when nothing is under it, and
/// that the viewport changed at all. Both exist for elements positioned from outside the tree.
/// </summary>
public class RootNotificationTests
{
    [Test]
    public void MovingThePointer_ReportsTheNewPosition()
    {
        UiRoot root = Empty();
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;

        root.PointerMoved(new Vector2Int(12, 34));

        Assert.Multiple(() =>
        {
            Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(12, 34) }));
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(12, 34)));
        });
    }

    [Test]
    public void MovingThePointerNowhere_ReportsNothing()
    {
        UiRoot root = Empty();
        root.PointerMoved(new Vector2Int(12, 34));
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;

        root.PointerMoved(new Vector2Int(12, 34));

        Assert.That(reported, Is.Empty);
    }

    [Test]
    public void PressingSomewhereNew_ReportsThePositionToo()
    {
        UiRoot root = Empty();
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;

        root.PointerPressed(new Vector2Int(5, 6), MouseButton.Left);

        Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(5, 6) }),
            "a press carries a position, and something following the pointer has to believe it");
    }

    [Test]
    public void APositionListener_RunsBeforeHitTesting()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(new Column { Children = { target } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        // Positioning from the pointer only works if the move is announced before the route decides
        // what is under it, or the tree is one event behind whatever follows the cursor.
        List<string> order = new();
        root.PointerPositionChanged += _ => order.Add("reported");
        target.WhenEntered = () => order.Add("entered");

        root.PointerMoved(new Vector2Int(10, 10));

        Assert.That(order, Is.EqualTo(new[] { "reported", "entered" }));
    }

    [Test]
    public void ResizingTheViewport_ReportsTheNewSize()
    {
        UiRoot root = Empty();
        List<Vector2Int> reported = new();
        root.ViewportChanged += reported.Add;

        root.SetViewportSize(new Vector2Int(640, 480));

        Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(640, 480) }));
    }

    [Test]
    public void ResizingToTheSameSize_ReportsNothing()
    {
        UiRoot root = Empty();
        List<Vector2Int> reported = new();
        root.ViewportChanged += reported.Add;

        root.SetViewportSize(new Vector2Int(320, 240));

        Assert.That(reported, Is.Empty);
    }

    [Test]
    public void AViewportListener_TakesEffectInTheSamePass()
    {
        UiRoot root = Empty();
        MeasuredBox child = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        root.AddLayer(new Element { Layout = AnchoredLayout.TopLeft, Children = { child } });
        root.Update();

        // A position derived from the viewport is an input to the pass that follows the resize.
        // Reported afterwards instead, it would place the popup a frame behind the window.
        root.ViewportChanged += size => child.Anchor = new Vector2Int(size.X / 2, size.Y / 2);

        root.SetViewportSize(new Vector2Int(640, 480));
        root.Update();

        Assert.That(child.Bounds, Is.EqualTo(new Rectangle(320, 240, 40, 20)));
    }

    private static UiRoot Empty()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }
}
