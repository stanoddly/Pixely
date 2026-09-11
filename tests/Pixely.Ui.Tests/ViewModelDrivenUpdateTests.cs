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

    [Test]
    public void PaintVersion_StandsStill_WhenARebuildPaintsTheSameQuads()
    {
        Button button = new() { Width = Sizing.Fixed(40), Height = Sizing.Fixed(20) };
        UiRoot root = Run(new Column { Children = { button } });
        root.Style = new UiStyle { Button = new ButtonAppearance { Background = new StateDrawables(new SolidDrawable(Fill)) } };
        root.Update();

        ulong painted = root.PaintVersion;
        ulong built = root.BuildVersion;

        // Hover invalidates paint, but the style resolves the same drawable for both states.
        root.PointerMoved(new Vector2Int(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(root.Update(), Is.True, "the hover still rebuilds");
            Assert.That(root.BuildVersion, Is.EqualTo(built + 1));
            Assert.That(root.PaintVersion, Is.EqualTo(painted), "but identical quads need no repaint");
        });
    }

    [Test]
    public void PaintVersion_Rises_WhenAQuadChanges()
    {
        MeasuredBox box = new(20, 20) { Background = new SolidDrawable(Fill) };
        UiRoot root = Run(new Column { Children = { box } });
        ulong painted = root.PaintVersion;

        box.Background = new SolidDrawable(new Color(200, 40, 40, 255));
        root.Update();

        Assert.That(root.PaintVersion, Is.EqualTo(painted + 1));
    }

    [Test]
    public void Instructions_KeepTheLastCompletedBuild_WhenABuildThrows()
    {
        ThrowingBox box = new() { Background = new SolidDrawable(Fill) };
        UiRoot root = Run(new Column { Children = { box } });
        PaintInstruction[] instructions = root.Instructions.ToArray();
        PaintBatch[] batches = root.Batches.ToArray();
        ulong painted = root.PaintVersion;

        box.ThrowOnNextPaint = true;
        box.Poke();

        Assert.Multiple(() =>
        {
            Assert.That(() => root.Update(), Throws.InvalidOperationException);
            Assert.That(root.Instructions, Is.EqualTo(instructions), "the half-painted list never replaces the completed one");
            Assert.That(root.Batches, Is.EqualTo(batches));
            Assert.That(root.PaintVersion, Is.EqualTo(painted));
        });
    }

    private sealed class ThrowingBox : Element
    {
        public bool ThrowOnNextPaint { get; set; }

        public ThrowingBox()
        {
            Width = Sizing.Fixed(20);
            Height = Sizing.Fixed(20);
        }

        public void Poke() => InvalidatePaint();

        protected override void PaintContent(PaintContext context)
        {
            if (ThrowOnNextPaint)
            {
                ThrowOnNextPaint = false;
                // A quad the completed build does not have, so the working list differs from it
                // and a root exposing the wrong list would show up.
                context.FillRectangle(Bounds, Colors.Red);
                throw new InvalidOperationException("paint failed");
            }
        }
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
