using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// What an <see cref="IPointerDragTarget"/> hears: a drag for every move while it holds a press, with
/// the button that press was, wherever the pointer has gone, and nothing once the press has ended.
/// </summary>
public class PointerDragTests
{
    [Test]
    public void AMoveWhileCaptured_IsADrag()
    {
        RecordingDragTarget target = Sized();
        UiRoot root = Rooted(target);

        root.PointerMoved(new Vector2Int(10, 10));
        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerMoved(new Vector2Int(12, 14));
        root.PointerReleased(new Vector2Int(12, 14), MouseButton.Left);
        root.PointerMoved(new Vector2Int(15, 15));

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left", "drag 12,14 Left", "release 12,14 Left inside=True" }), "no drag before the press or after the release");
    }

    [Test]
    public void ADrag_FollowsThePointerOffTheElement_BeforeHoverLeavesIt()
    {
        RecordingDragTarget target = Sized();
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerMoved(new Vector2Int(200, 200));
        root.PointerMoved(new Vector2Int(10, 10));

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left", "drag 200,200 Left", "leave", "drag 10,10 Left", "enter 10,10" }));
    }

    [Test]
    public void TwoButtonsHeldByTwoTargets_EachGetTheirOwnDrag()
    {
        RecordingDragTarget first = Sized();
        RecordingDragTarget second = Sized();
        second.Accepts.Clear();
        second.Accepts.Add(MouseButton.Right);
        UiRoot root = InRow(first, second);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Right);
        root.PointerMoved(new Vector2Int(30, 30));

        Assert.Multiple(() =>
        {
            Assert.That(first.Calls, Does.Contain("drag 30,30 Left").And.Not.Contain("drag 30,30 Right"));
            Assert.That(second.Calls, Does.Contain("drag 30,30 Right").And.Not.Contain("drag 30,30 Left"));
        });
    }

    [Test]
    public void AfterACancel_NoDragArrives()
    {
        RecordingDragTarget target = Sized();
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerLeft();
        root.PointerMoved(new Vector2Int(12, 12));

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left", "cancel Left", "leave", "enter 12,12" }));
    }

    [Test]
    public void APlainPointerTarget_HearsNothingOfAMove()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Rooted(target);

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerMoved(new Vector2Int(12, 12));

        Assert.That(target.Calls, Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left" }));
    }

    [Test]
    public void ADragCallbackThatHidesItsElement_LeavesItUnhoveredAndTheNextBuildCancelsIt()
    {
        RecordingDragTarget target = Sized();
        UiRoot root = Rooted(target);
        target.WhenDragged = () => target.IsVisible = false;

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerMoved(new Vector2Int(12, 12));
        int beforeBuild = target.Calls.Count;
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(target.Calls.Take(beforeBuild), Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left", "drag 12,12 Left", "leave" }), "hover is settled after the drag");
            Assert.That(target.Calls.Skip(beforeBuild), Is.EqualTo(new[] { "cancel Left" }));
        });
    }

    [Test]
    public void ADragCallbackThatMovesThePointerItself_LeavesTheRestToTheNestedRoute()
    {
        RecordingDragTarget first = Sized();
        RecordingDragTarget second = Sized();
        second.Accepts.Clear();
        second.Accepts.Add(MouseButton.Right);
        UiRoot root = InRow(first, second);
        bool moved = false;
        first.WhenDragged = () =>
        {
            if (moved)
            {
                return;
            }

            moved = true;
            root.PointerMoved(new Vector2Int(33, 33));
        };

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerPressed(new Vector2Int(50, 10), MouseButton.Right);
        root.PointerMoved(new Vector2Int(30, 30));

        Assert.Multiple(() =>
        {
            Assert.That(first.Calls.Where(call => call.StartsWith("drag")), Is.EqualTo(new[] { "drag 30,30 Left", "drag 33,33 Left" }));
            Assert.That(second.Calls.Where(call => call.StartsWith("drag")), Is.EqualTo(new[] { "drag 33,33 Right" }), "the outer move is abandoned once the nested one has delivered");
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(33, 33)));
        });
    }

    [Test]
    public void ADragCallbackThatPressesTheSameButtonElsewhere_HandsTheGestureOver()
    {
        RecordingDragTarget first = Sized();
        RecordingDragTarget second = Sized();
        UiRoot root = InRow(first, second);
        bool pressed = false;
        first.WhenDragged = () =>
        {
            if (pressed)
            {
                return;
            }

            pressed = true;
            root.PointerPressed(new Vector2Int(50, 10), MouseButton.Left);
        };

        root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);
        root.PointerMoved(new Vector2Int(30, 30));
        root.PointerMoved(new Vector2Int(31, 31));

        Assert.Multiple(() =>
        {
            Assert.That(first.Calls, Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left", "drag 30,30 Left", "cancel Left", "leave" }));
            Assert.That(second.Calls, Is.EqualTo(new[] { "enter 50,10", "press 50,10 Left", "drag 31,31 Left", "leave" }), "the drag arrives before hover notices the pointer is off it");
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
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }

    private static RecordingDragTarget Sized() => new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };

    private static UiRoot Rooted(Element content)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { content } });
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }
}
