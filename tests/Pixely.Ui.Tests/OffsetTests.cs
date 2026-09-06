namespace Pixely.Ui.Tests;

/// <summary>
/// An offset moves an element without telling its parent, which is what makes it cheap: the whole
/// point is that following the pointer costs an arrange and never a measure.
/// </summary>
public class OffsetTests
{
    [Test]
    public void AnOffsetChild_MovesWithinAStack()
    {
        MeasuredBox first = Sized(40, 20);
        MeasuredBox second = Sized(40, 20);
        second.Offset = new Vector2Int(5, 7);
        Element host = new Column { Children = { first, second } };

        Layout.Run(host, 320, 240);

        Assert.Multiple(() =>
        {
            Assert.That(first.Bounds, Is.EqualTo(new Rectangle(0, 0, 40, 20)));
            Assert.That(second.Bounds, Is.EqualTo(new Rectangle(5, 27, 40, 20)));
        });
    }

    [Test]
    public void AnOffsetChild_DoesNotMoveItsSiblings()
    {
        MeasuredBox first = Sized(40, 20);
        MeasuredBox second = Sized(40, 20);
        Element host = new Column { Children = { first, second } };
        Layout.Run(host, 320, 240);

        first.Offset = new Vector2Int(0, 100);
        Layout.Run(host, 320, 240);

        Assert.That(second.Bounds, Is.EqualTo(new Rectangle(0, 20, 40, 20)),
            "the parent still allocates from the size it measured, so the slot the offset element left is not reclaimed");
    }

    [Test]
    public void ChangingAnOffset_ArrangesWithoutMeasuring()
    {
        MeasuredBox child = Sized(40, 20);
        Element host = new Column { Children = { child } };
        Layout.Run(host, 320, 240);
        int measures = child.MeasureCount;
        int arranges = child.ArrangeCount;

        child.Offset = new Vector2Int(11, 0);
        Layout.Run(host, 320, 240);

        Assert.Multiple(() =>
        {
            Assert.That(child.MeasureCount, Is.EqualTo(measures), "an offset is not an input to measure");
            Assert.That(child.ArrangeCount, Is.EqualTo(arranges + 1));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(11, 0, 40, 20)));
        });
    }

    [Test]
    public void AnOffsetChild_DoesNotGrowAParentThatFitsIt()
    {
        MeasuredBox child = Sized(40, 20);
        child.Offset = new Vector2Int(100, 100);
        Element host = new Column { Children = { child } };

        Vector2Int size = Layout.MeasureUnbounded(host);

        Assert.That(size, Is.EqualTo(new Vector2Int(40, 20)));
    }

    [Test]
    public void AnOffsetChild_StaysHittableOutsideItsParent()
    {
        RecordingPointerTarget child = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        child.Offset = new Vector2Int(100, 100);
        UiRoot root = new();
        root.AddLayer(new Column { Children = { child } });
        root.SetViewportSize(new Vector2Int(320, 240));
        root.Update();

        root.PointerMoved(new Vector2Int(110, 110));

        Assert.That(child.Calls, Is.EqualTo(new[] { "enter 110,110" }),
            "hit testing is not pruned by an ancestor's bounds, so an element is findable where it was actually drawn");
    }

    [Test]
    public void AnOffsetChild_IsStillClippedByAnAncestor()
    {
        MeasuredBox child = Sized(40, 20);
        child.Offset = new Vector2Int(100, 100);
        Element clipper = new()
        {
            ClipsContent = true,
            Width = Sizing.Fixed(60),
            Height = Sizing.Fixed(40),
            Children = { child }
        };

        // Not the layer itself, which a root arranges at the whole viewport whatever its sizing says.
        Layout.Run(new Column { Children = { clipper } }, 320, 240);

        Assert.Multiple(() =>
        {
            Assert.That(child.EffectiveClip, Is.EqualTo(new Rectangle(0, 0, 60, 40)));
            Assert.That(child.Bounds, Is.EqualTo(new Rectangle(100, 100, 40, 20)),
                "moving out of a clipping ancestor moves out of what it shows");
        });
    }

    private static MeasuredBox Sized(int width, int height) =>
        new() { Width = Sizing.Fixed(width), Height = Sizing.Fixed(height) };
}
