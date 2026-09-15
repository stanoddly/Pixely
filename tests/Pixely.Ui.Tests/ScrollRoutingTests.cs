using System.Numerics;
using Pixely.Input;

namespace Pixely.Ui.Tests;

/// <summary>
/// Where a wheel goes: to the topmost pointer or scroll target under it, and from there up through
/// its ancestors, each taking the axes it can use.
/// </summary>
public class ScrollRoutingTests
{
    private static readonly Vector2 Down = new(0f, -1f);

    [Test]
    public void AWheelOverAnOrdinaryChild_ReachesTheScrollTargetAroundIt()
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { new MeasuredBox(100, 100) } };
        UiRoot root = Rooted(list);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(list.Calls, Is.EqualTo(new[] { "scroll 10,10 0,-1" }));
        });
    }

    [Test]
    public void AWheelOverAPointerTargetInsideAList_ReachesTheList()
    {
        RecordingPointerTarget button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { button } };
        UiRoot root = Rooted(list);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(list.Calls, Has.Count.EqualTo(1));
            Assert.That(button.Calls, Is.EqualTo(new[] { "enter 10,10" }), "hover follows the wheel's position");
        });
    }

    [Test]
    public void AnInnerTargetThatRefuses_PassesToTheOuterOne()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.None };
        RecordingScrollTarget outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        UiRoot root = Rooted(outer);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(inner.Calls, Has.Count.EqualTo(1), "offered first, being on top");
            Assert.That(outer.Calls, Has.Count.EqualTo(1), "and offered what the inner one refused");
        });
    }

    [Test]
    public void AnInnerTargetThatTakesOneAxis_LeavesTheOtherToTheOuterOne()
    {
        RecordingScrollTarget strip = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.Horizontal };
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { strip } };
        UiRoot root = Rooted(list);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), new Vector2(0.5f, -1f));

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True);
            Assert.That(strip.Calls, Is.EqualTo(new[] { "scroll 10,10 0.5,-1" }));
            Assert.That(list.Calls, Is.EqualTo(new[] { "scroll 10,10 0,-1" }), "the horizontal component stopped at the strip");
        });
    }

    [Test]
    public void AnInnerTargetThatTakesEverything_StopsTheWalk()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50) };
        RecordingScrollTarget outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        UiRoot root = Rooted(outer);

        root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.That(outer.Calls, Is.Empty);
    }

    [Test]
    public void AWheelOverALayerAbove_DoesNotReachTheListBeneath()
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        RecordingPointerTarget dialog = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = Rooted(list);
        root.AddLayer(new Column { Children = { dialog } });
        root.Update();

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False, "the dialog takes no wheel, and the list is not its ancestor");
            Assert.That(list.Calls, Is.Empty);
        });
    }

    [Test]
    public void AWheelOverNothing_IsNotConsumed()
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = Rooted(list);

        bool consumed = root.PointerScrolled(new Vector2Int(200, 200), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(list.Calls, Is.Empty);
            Assert.That(root.PointerPosition, Is.EqualTo(new Vector2Int(200, 200)), "the pointer still moved there");
        });
    }

    [Test]
    public void AWheel_ReportsThePointerPosition()
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = Rooted(list);
        List<Vector2Int> reported = new();
        root.PointerPositionChanged += reported.Add;

        root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.That(reported, Is.EqualTo(new[] { new Vector2Int(10, 10) }));
    }

    [Test]
    public void AZeroDelta_IsOfferedToNothing()
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = Rooted(list);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Vector2.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(list.Calls, Is.Empty);
        });
    }

    [Test]
    public void AWheelOverAPlainPanelAbove_ReachesTheListBeneath()
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = Rooted(list);
        root.AddLayer(new Column { Children = { new Element { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) } } });
        root.Update();

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True, "a panel that takes no input is transparent to the wheel as it is to the pointer");
            Assert.That(list.Calls, Has.Count.EqualTo(1));
        });
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void AHiddenOrDisabledTarget_IsNotOffered(bool hidden, bool disabled)
    {
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = Rooted(list);
        list.IsVisible = !hidden;
        list.IsEnabled = !disabled;
        root.Update();

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(list.Calls, Is.Empty);
        });
    }

    [Test]
    public void AScrollOnlyTarget_IsTransparentToThePointer()
    {
        RecordingPointerTarget behind = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = new();
        root.AddLayer(new Column { Children = { behind } });
        root.AddLayer(new Column { Children = { list } });
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();

        root.PointerMoved(new Vector2Int(10, 10));
        bool consumed = root.PointerPressed(new Vector2Int(10, 10), MouseButton.Left);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True, "the press went through to what takes the pointer");
            Assert.That(behind.Calls, Is.EqualTo(new[] { "enter 10,10", "press 10,10 Left" }));
        });
    }

    [Test]
    public void AHoverCallbackThatRoutesThePointer_AbandonsTheWheel()
    {
        RecordingPointerTarget button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        RecordingScrollTarget list = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { button } };
        UiRoot root = Rooted(list);
        root.PointerMoved(new Vector2Int(200, 200));
        button.WhenEntered = () => root.PointerMoved(new Vector2Int(200, 200));

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False, "the pointer is no longer where the wheel happened");
            Assert.That(list.Calls, Is.Empty);
        });
    }

    [Test]
    public void AScrollCallbackThatRoutesThePointer_KeepsWhatWasTakenAndStopsTheWalk()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.Horizontal };
        RecordingScrollTarget outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        UiRoot root = Rooted(outer);
        inner.WhenScrolled = () => root.PointerMoved(new Vector2Int(200, 200));

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), new Vector2(1f, -1f));

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.True, "the inner target took the horizontal component");
            Assert.That(outer.Calls, Is.Empty, "and the vertical one went nowhere, since the pointer moved on");
        });
    }

    [Test]
    public void AScrollCallbackThatScrollsElsewhere_DeliversEachWheelOnce()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.None };
        RecordingScrollTarget outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        RecordingScrollTarget other = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { outer, other } });
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();
        inner.WhenScrolled = () =>
        {
            inner.WhenScrolled = null;
            root.PointerScrolled(new Vector2Int(150, 10), Down);
        };

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False, "the outer wheel was abandoned once the pointer moved on");
            Assert.That(inner.Calls, Is.EqualTo(new[] { "scroll 10,10 0,-1" }));
            Assert.That(other.Calls, Is.EqualTo(new[] { "scroll 150,10 0,-1" }), "the nested wheel went where it was aimed, once");
            Assert.That(outer.Calls, Is.Empty, "and the outer walk did not carry on over a chain the nested wheel rebuilt");
        });
    }

    [Test]
    public void AScrollCallbackThatDetachesItsAncestor_StopsTheWalk()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.None };
        RecordingScrollTarget outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        UiRoot root = Rooted(outer);
        inner.WhenScrolled = () => outer.Parent!.Children.Remove(outer);

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(outer.Calls, Is.Empty, "input can no longer reach it");
        });
    }

    [Test]
    public void AScrollCallbackThatHidesItsAncestor_StopsTheWalk()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.None };
        RecordingScrollTarget outer = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        UiRoot root = Rooted(outer);
        inner.WhenScrolled = () => outer.IsVisible = false;

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(outer.Calls, Is.Empty);
        });
    }

    [Test]
    public void AScrollCallbackThatReparentsItsElement_DoesNotSendTheRestUpTheNewChain()
    {
        RecordingScrollTarget inner = new() { Width = Sizing.Fixed(50), Height = Sizing.Fixed(50), Takes = ScrollAxes.None };
        RecordingScrollTarget original = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100), Children = { inner } };
        RecordingScrollTarget adoptive = new() { Width = Sizing.Fixed(100), Height = Sizing.Fixed(100) };
        UiRoot root = new();
        root.AddLayer(new Row { Children = { original, adoptive } });
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();

        inner.WhenScrolled = () =>
        {
            original.Children.Remove(inner);
            adoptive.Children.Add(inner);
        };

        bool consumed = root.PointerScrolled(new Vector2Int(10, 10), Down);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.False);
            Assert.That(adoptive.Calls, Is.Empty, "the wheel was not over it");
            Assert.That(original.Calls, Is.Empty, "and the chain it was over is gone");
        });
    }

    private static UiRoot Rooted(Element content)
    {
        UiRoot root = new();
        root.AddLayer(new Column { Children = { content } });
        root.SetTargetSize(new Vector2Int(320, 240));
        root.Update();
        return root;
    }
}
