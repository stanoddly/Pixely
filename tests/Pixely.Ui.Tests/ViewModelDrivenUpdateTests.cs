using Pixely.Gpu;

namespace Pixely.Ui.Tests;

/// <summary>
/// The pattern a view model driven UI relies on: the tree is built once, a change to one element
/// updates only what it has to, and no change at all costs nothing.
/// </summary>
public class ViewModelDrivenUpdateTests
{
    private static readonly Color Fill = new(90, 190, 120, 255);

    [Test]
    public void Update_WithNoChanges_DoesNothing()
    {
        MeasuredBox box = new(20, 20) { Background = new SolidDrawable(Fill) };
        UiRoot root = Run(new Column { Children = { box } });

        Assert.That(root.Update(), Is.False, "a second update with nothing dirty must not rebuild");
    }

    [Test]
    public void Update_AfterAPropertyChange_RebuildsOnce()
    {
        MeasuredBox box = new(20, 20) { Background = new SolidDrawable(Fill) };
        UiRoot root = Run(new Column { Children = { box } });

        box.IntrinsicSize = new Vector2Int(40, 40);

        Assert.Multiple(() =>
        {
            Assert.That(root.Update(), Is.True);
            Assert.That(root.Update(), Is.False, "the rebuild clears the dirty state");
        });
    }

    [Test]
    public void ChangingAWidth_ReArrangesWithoutReMeasuringSiblings()
    {
        MeasuredBox bar = new(10, 18) { Background = new SolidDrawable(Fill) };
        MeasuredBox sibling = new(30, 18) { Background = new SolidDrawable(Fill) };
        Column tree = new() { Children = { bar, sibling } };
        UiRoot root = Run(tree, 200, 100);

        int siblingMeasures = sibling.MeasureCount;

        // What a view model driving a health bar actually does.
        bar.Width = Sizing.Fixed(120);
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(bar.Bounds.Width, Is.EqualTo(120));
            Assert.That(sibling.MeasureCount, Is.EqualTo(siblingMeasures),
                "resizing one element must not re-measure the ones next to it");
        });
    }

    [Test]
    public void ShowingAHiddenElement_AddsItsQuadWithoutTouchingTheRest()
    {
        MeasuredBox always = new(20, 20) { Background = new SolidDrawable(Fill) };
        MeasuredBox gameOver = new(20, 20) { Background = new SolidDrawable(Fill), IsVisible = false };
        UiRoot root = Run(new Column { Children = { always, gameOver } });

        int before = root.Instructions.Count;

        gameOver.IsVisible = true;
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(root.Instructions, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AssigningAnUnchangedValue_LeavesTheTreeClean()
    {
        MeasuredBox box = new(20, 20) { Background = new SolidDrawable(Fill) };
        UiRoot root = Run(new Column { Children = { box } });

        // A view syncing every field on any change relies on this: writing the same value is free.
        box.IntrinsicSize = new Vector2Int(20, 20);
        box.Width = Sizing.Fit;
        box.IsVisible = true;

        Assert.That(root.Update(), Is.False);
    }

    [Test]
    public void RepeatedUpdates_KeepInstructionsStable()
    {
        MeasuredBox box = new(20, 20) { Background = new SolidDrawable(Fill) };
        UiRoot root = Run(new Column { Children = { box } });

        Rectangle first = root.Instructions[0].Area;

        box.IntrinsicSize = new Vector2Int(30, 30);
        root.Update();
        box.IntrinsicSize = new Vector2Int(20, 20);
        root.Update();

        Assert.That(root.Instructions[0].Area, Is.EqualTo(first), "returning to a previous state reproduces it exactly");
    }

    private static UiRoot Run(Element tree, int width = 100, int height = 100)
    {
        UiRoot root = new();
        root.AddLayer(tree);
        root.SetViewportSize(new Vector2Int(width, height));
        root.Update();
        return root;
    }
}
