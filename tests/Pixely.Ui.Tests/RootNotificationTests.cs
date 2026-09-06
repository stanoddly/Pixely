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
    public void APositionListener_RunsOnceTheRouteHasFinished()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(new Column { Children = { target } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        // A listener may route the pointer itself, so it runs when there is no route in flight for it
        // to interfere with. Nothing is lost by the wait: what follows the pointer is not a hit target
        // and only needs the position before the next pass, which has not run yet either.
        List<string> order = new();
        root.PointerPositionChanged += _ => order.Add("reported");
        target.WhenEntered = () => order.Add("entered");

        root.PointerMoved(new Vector2Int(10, 10));

        Assert.That(order, Is.EqualTo(new[] { "entered", "reported" }));
    }

    [Test]
    public void APositionListenerThatRoutesAgain_LeavesEverySubscriberOnTheLatestPosition()
    {
        UiRoot root = Empty();
        List<Vector2Int> second = new();
        bool routed = false;

        root.PointerPositionChanged += _ =>
        {
            if (routed)
            {
                return;
            }

            routed = true;
            root.PointerMoved(new Vector2Int(99, 99));
        };
        root.PointerPositionChanged += second.Add;

        root.PointerMoved(new Vector2Int(12, 34));

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(new[] { new Vector2Int(99, 99) }),
                "a subscriber is never told a position an earlier one has already moved on from");
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(99, 99)));
        });
    }

    [Test]
    public void APositionListenerThatPresses_DoesNotHijackAReleaseInFlight()
    {
        RecordingPointerTarget held = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        RecordingPointerTarget other = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { held, other } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        held.Calls.Clear();

        // Reported from inside the route, this press would land before the release had read which
        // gesture it was ending. The release would then find the brand new capture and end that
        // instead, and the gesture it was actually for would hear only a cancel.
        bool pressed = false;
        root.PointerPositionChanged += _ =>
        {
            if (pressed)
            {
                return;
            }

            pressed = true;
            root.PointerPressed(new Vector2Int(50, 10), MouseButton.Left);
        };

        root.PointerReleased(new Vector2Int(50, 10), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.True, "the listener ran at all");
            Assert.That(held.Calls, Does.Contain("release 50,10 Left inside=False"), "the release ends the gesture it was made for");
            Assert.That(other.Calls, Does.Contain("press 50,10 Left"), "and the gesture the listener started is the one still held");
            Assert.That(other.Calls, Does.Not.Contain("release 50,10 Left inside=True"));
        });
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

    [Test]
    public void AListenerThatMovesAwayAndBack_DoesNotLeaveAnEarlierDeliveryToFinish()
    {
        UiRoot root = Empty();
        List<Vector2Int> second = new();
        bool routed = false;

        root.PointerPositionChanged += _ =>
        {
            if (routed)
            {
                return;
            }

            routed = true;
            root.PointerMoved(new Vector2Int(99, 99));
            root.PointerMoved(new Vector2Int(12, 34));
        };
        root.PointerPositionChanged += second.Add;

        root.PointerMoved(new Vector2Int(12, 34));

        Assert.That(second, Is.EqualTo(new[] { new Vector2Int(99, 99), new Vector2Int(12, 34) }),
            "the position ends where it started, but the delivery it interrupted is finished with all the same");
    }

    [Test]
    public void TheFirstRouteToTheOrigin_ReportsNothing()
    {
        UiRoot root = Empty();
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;

        root.PointerMoved(new Vector2Int(0, 0));

        Assert.That(reported, Is.Empty, "the pointer starts there, so arriving there is not a change");
    }

    [Test]
    public void AViewportListenerThatResizesAgain_LeavesEverySubscriberOnTheLatestSize()
    {
        UiRoot root = Empty();
        List<Vector2Int> second = new();
        bool resized = false;

        root.ViewportChanged += _ =>
        {
            if (resized)
            {
                return;
            }

            resized = true;
            root.SetViewportSize(new Vector2Int(800, 600));
        };
        root.ViewportChanged += second.Add;

        root.SetViewportSize(new Vector2Int(640, 480));

        Assert.That(second, Is.EqualTo(new[] { new Vector2Int(800, 600) }));
    }

    [Test]
    public void SubscribingFromInsideADelivery_TakesEffectOnTheNextOne()
    {
        UiRoot root = Empty();
        List<Vector2Int> late = new();
        bool subscribed = false;

        root.PointerPositionChanged += _ =>
        {
            if (subscribed)
            {
                return;
            }

            subscribed = true;
            root.PointerPositionChanged += late.Add;
        };

        root.PointerMoved(new Vector2Int(1, 1));
        root.PointerMoved(new Vector2Int(2, 2));

        Assert.That(late, Is.EqualTo(new[] { new Vector2Int(2, 2) }));
    }

    private static UiRoot Empty()
    {
        UiRoot root = new();
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }
}
