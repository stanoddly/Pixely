using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// Covers what a button means to the router: which ones a target takes, that a declined one is left
/// for whatever the UI is drawn over, and that two gestures in flight at once stay separate.
/// </summary>
public class PointerButtonTests
{
    [Test]
    public void ADeclinedPress_IsNotConsumedAndTakesNoCapture()
    {
        RecordingPointerTarget target = Sized();
        UiRoot root = Rooted(target);

        bool consumed = root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        bool released = root.PointerReleased(new Vector2Int(10, 10), MouseButton.Right);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False, "a declined press must reach what is underneath");
            Assert.That(released, Is.False, "declining takes no capture, so there is no release to route");
            Assert.That(target.Calls, Is.EqualTo(new[] { "enter 10,10", "declined Right" }));
        });
    }

    [Test]
    public void AnOrdinaryButton_IgnoresTheRightButton()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(button);
        int clicks = 0;
        button.Clicked += () => clicks++;

        bool consumed = root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Right);

        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.Zero);
            Assert.That(consumed, Is.False);
        });
    }

    [Test]
    public void TwoButtonsHeldAtOnce_ReleaseIndependently()
    {
        RecordingPointerTarget target = Sized();
        target.Accepts.Add(MouseButton.Right);
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);

        Assert.That(target.Calls, Is.EqualTo(new[]
        {
            "enter 10,10",
            "press 10,10 Left",
            "press 10,10 Right",
            "release 10,10 Right inside=True",
            "release 10,10 Left inside=True"
        }));
    }

    [Test]
    public void ASecondPressOfTheSameButton_CancelsOnlyItsOwnCapture()
    {
        RecordingPointerTarget target = Sized();
        target.Accepts.Add(MouseButton.Right);
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);

        Assert.That(target.Calls, Is.EqualTo(new[]
        {
            "enter 10,10",
            "press 10,10 Left",
            "press 10,10 Right",
            "cancel Right",
            "press 10,10 Right"
        }), "the left gesture is untouched by a second right press");
    }

    [Test]
    public void LeavingTheWindow_CancelsEveryHeldButton()
    {
        RecordingPointerTarget target = Sized();
        target.Accepts.Add(MouseButton.Right);
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerLeft();

        Assert.That(target.Calls, Is.EqualTo(new[]
        {
            "enter 10,10",
            "press 10,10 Left",
            "press 10,10 Right",
            "cancel Left",
            "cancel Right",
            "leave"
        }));
    }

    [Test]
    public void HidingACapturedTarget_CancelsOnlyItsOwnButtons()
    {
        RecordingPointerTarget held = Sized();
        held.Accepts.Add(MouseButton.Right);
        UiRoot root = Rooted(held);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        held.Calls.Clear();

        held.IsVisible = false;
        root.Update();

        Assert.That(held.Calls, Is.EqualTo(new[] { "cancel Left", "cancel Right", "leave" }));
    }

    [Test]
    public void AReleaseWithNoCaptureForThatButton_IsNotConsumed()
    {
        RecordingPointerTarget target = Sized();
        target.Accepts.Add(MouseButton.Right);
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        bool released = root.PointerReleased(new Vector2Int(10, 10), MouseButton.Right);

        Assert.Multiple(() =>
        {
            Assert.That(released, Is.False);
            Assert.That(target.Calls, Does.Not.Contain("release 10,10 Right inside=True"));
        });
    }

    [Test]
    public void HoverFollowsTheLeftCapture_WhileAnotherButtonIsAlsoHeld()
    {
        RecordingPointerTarget target = Sized();
        target.Accepts.Add(MouseButton.Right);
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        target.Calls.Clear();

        // Dragged off: the left capture owns hover, so this leaves rather than staying lit.
        root.PointerMoved(new Vector2Int(300, 200));

        Assert.That(target.Calls, Is.EqualTo(new[] { "leave" }));
    }

    [Test]
    public void WithNoLeftCapture_HoverFollowsTheOldestHeldButton()
    {
        RecordingPointerTarget first = Sized();
        first.Accepts.Add(MouseButton.Right);
        RecordingPointerTarget second = Sized();
        second.Accepts.Add(MouseButton.Middle);
        UiRoot root = InRow(first, second);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Middle);
        first.Calls.Clear();
        second.Calls.Clear();

        root.PointerMoved(new Vector2Int(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(first.Calls, Is.EqualTo(new[] { "enter 10,10" }), "the right press came first, so it is what hover tracks");
            Assert.That(second.Calls, Is.Empty);
        });
    }

    [Test]
    public void ReleasingTheOldestCapture_HandsHoverToTheNextOne()
    {
        RecordingPointerTarget first = Sized();
        first.Accepts.Add(MouseButton.Right);
        RecordingPointerTarget second = Sized();
        second.Accepts.Add(MouseButton.Middle);
        UiRoot root = InRow(first, second);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Middle);
        root.PointerMoved(new Vector2Int(50, 10));
        second.Calls.Clear();

        root.PointerReleased(new Vector2Int(50, 10), MouseButton.Right);

        Assert.That(second.Calls, Does.Contain("enter 50,10"),
            "with the older gesture gone the middle capture owns hover, so the element under the pointer lights up again");
    }

    [Test]
    public void ANestedPressFromACancelCallback_KeepsItsOwnCapture()
    {
        RecordingPointerTarget held = Sized();
        RecordingPointerTarget other = Sized();
        UiRoot root = InRow(held, other);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);

        // Pressing left again cancels the gesture held has. That cancel presses left itself, which
        // captures the other element; the outer press must not carry on and overwrite it, leaving a
        // gesture that can never be released.
        bool pressedAgain = false;
        held.WhenCancelled = () =>
        {
            if (pressedAgain)
            {
                return;
            }

            pressedAgain = true;
            root.PointerPressed(new Vector2Int(50, 10), MouseButton.Left);
        };

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        other.Calls.Clear();
        root.PointerReleased(new Vector2Int(50, 10), MouseButton.Left);

        Assert.That(other.Calls, Does.Contain("release 50,10 Left inside=True"),
            "the gesture the cancel callback started is the one that is still held");
    }

    [Test]
    public void LeavingTheWindow_KeepsAGestureACancelCallbackStarted()
    {
        RecordingPointerTarget held = Sized();
        held.Accepts.Add(MouseButton.Right);
        RecordingPointerTarget other = Sized();
        other.Accepts.Add(MouseButton.Right);
        UiRoot root = InRow(held, other);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);

        // Right sorts after left, so a sweep that cancelled whatever it found in each slot would
        // reach this replacement and end it. Which button it happens to be must not decide that.
        bool pressedAgain = false;
        held.WhenCancelled = () =>
        {
            if (pressedAgain)
            {
                return;
            }

            pressedAgain = true;
            root.PointerPressed(new Vector2Int(50, 10), MouseButton.Right);
        };

        root.PointerLeft();

        Assert.Multiple(() =>
        {
            Assert.That(other.Calls, Does.Contain("press 50,10 Right"));
            Assert.That(other.Calls, Does.Not.Contain("cancel Right"));
        });
    }

    [Test]
    public void AnEnterCallbackThatDetachesItsElement_DoesNotLeaveItHovered()
    {
        RecordingPointerTarget target = Sized();
        UiRoot root = Rooted(target);
        root.PointerMoved(new Vector2Int(300, 200));

        target.WhenEntered = () => target.IsVisible = false;

        root.PointerMoved(new Vector2Int(10, 10));

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 10,10", "leave" }),
            "an element that removes itself on the way in is not left holding hover");
    }

    [Test]
    public void APressAcceptedByACallbackThatDetachesIt_IsCancelledRatherThanCaptured()
    {
        RecordingPointerTarget target = Sized();
        UiRoot root = Rooted(target);

        // Accepting and then leaving the tree in the same callback: capture cannot be installed, so
        // the press has to be taken back rather than left as a gesture nothing can end.
        target.WhenPressed = () =>
        {
            target.IsVisible = false;
            root.Update();
        };

        bool consumed = root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        bool released = root.PointerReleased(new Vector2Int(10, 10), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True, "the target did take the press");
            Assert.That(released, Is.False, "but nothing was captured, so there is no release to route");
            Assert.That(target.Calls, Does.Contain("cancel Left"));
        });
    }

    [Test]
    public void ADeclinedPress_DoesNotFallThroughToWhatIsBehindIt()
    {
        RecordingPointerTarget behind = Sized();
        behind.Accepts.Add(MouseButton.Right);
        RecordingPointerTarget front = Sized();

        UiRoot root = new();
        root.AddLayer(new Overlay { Children = { behind, front } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        bool consumed = root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(front.Calls, Does.Contain("declined Right"));
            Assert.That(behind.Calls, Does.Not.Contain("press 10,10 Right"),
                "hit testing stays topmost-only: declining hands the button to the game, not to the element below");
        });
    }

    [Test]
    public void APressOnASecondElement_TakesOverHoverFromAnOlderCaptureElsewhere()
    {
        RecordingPointerTarget held = Sized();
        held.Accepts.Add(MouseButton.Right);
        RecordingPointerTarget other = Sized();
        UiRoot root = InRow(held, other);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);
        root.PointerMoved(new Vector2Int(50, 10));
        other.Calls.Clear();

        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Left);

        Assert.That(other.Calls, Is.EqualTo(new[] { "enter 50,10", "press 50,10 Left" }),
            "the left capture owns hover, so this element stays lit even though an older right capture is held elsewhere");
    }

    [Test]
    public void ACancelCallbackThatStartsAGestureElsewhere_KeepsIt()
    {
        RecordingPointerTarget held = Sized();
        held.Accepts.Add(MouseButton.Right);
        RecordingPointerTarget other = Sized();
        other.Accepts.Add(MouseButton.Right);
        UiRoot root = InRow(held, other);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Right);

        // Hiding the target makes both of its captures unreachable at once. The first cancel starts a
        // right-button gesture on an element that is still reachable, and the sweep must leave it
        // alone when it reaches that button rather than ending a gesture that has only just begun.
        bool pressedAgain = false;
        held.WhenCancelled = () =>
        {
            if (pressedAgain)
            {
                return;
            }

            pressedAgain = true;
            root.PointerPressed(new Vector2Int(50, 10), MouseButton.Right);
        };

        held.IsVisible = false;
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(other.Calls, Does.Contain("press 50,10 Right"), "the new gesture started");
            Assert.That(other.Calls, Does.Not.Contain("cancel Right"), "and survived the sweep");
        });
    }

    /// <summary>
    /// Side by side, with the first inside a fixed-size holder so hiding it does not slide the
    /// second one out from under the coordinates a test is pressing at.
    /// </summary>
    private static UiRoot InRow(Element first, Element second)
    {
        Element holder = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20), Children = { first } };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { holder, second } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }

    private static RecordingPointerTarget Sized() =>
        new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };

    private static UiRoot Rooted(Element content)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { content } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }
}
