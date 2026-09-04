using Pixely.Gpu;

namespace Pixely.Ui.Tests;

public class PaintBatcherTests
{
    private static readonly Color Red = new(255, 0, 0, 255);

    [Test]
    public void Batches_ConsecutiveSolidFills_AreOneBatch()
    {
        Column root = new()
        {
            Children =
            {
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) },
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) },
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) }
            }
        };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.Multiple(() =>
        {
            Assert.That(uiRoot.Instructions, Has.Count.EqualTo(3));
            Assert.That(uiRoot.Batches, Has.Count.EqualTo(1), "solid fills share the renderer's white texture");
            Assert.That(uiRoot.Batches[0].Count, Is.EqualTo(3));
        });
    }

    [Test]
    public void Batches_SplitWhenTheClipChanges()
    {
        Column root = new()
        {
            Children =
            {
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) },
                new ClipBorder
                {
                    Width = Sizing.Fixed(30),
                    Height = Sizing.Fixed(30),
                    Content = new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) }
                },
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) }
            }
        };

        UiRoot uiRoot = Run(root, 100, 100);

        Assert.That(uiRoot.Batches, Has.Count.EqualTo(3), "a clip change breaks the run and is restored after it");
    }

    [Test]
    public void Batches_EmptyInstructionList_ProducesNoBatches()
    {
        UiRoot uiRoot = Run(new Column(), 100, 100);

        Assert.That(uiRoot.Batches, Is.Empty);
    }

    [Test]
    public void Batches_CoverEveryInstructionExactlyOnceInOrder()
    {
        Column root = new()
        {
            Children =
            {
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) },
                new ClipBorder
                {
                    Width = Sizing.Fixed(30),
                    Height = Sizing.Fixed(30),
                    Content = new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) }
                },
                new MeasuredBox(10, 10) { Background = Drawable.Solid(Red) }
            }
        };

        UiRoot uiRoot = Run(root, 100, 100);

        int expectedStart = 0;
        foreach (PaintBatch batch in uiRoot.Batches)
        {
            Assert.That(batch.Start, Is.EqualTo(expectedStart));
            Assert.That(batch.Count, Is.GreaterThan(0));
            expectedStart += batch.Count;
        }

        Assert.That(expectedStart, Is.EqualTo(uiRoot.Instructions.Count));
    }

    private static UiRoot Run(Element root, int width, int height)
    {
        UiRoot uiRoot = new();
        uiRoot.AddLayer(root);
        uiRoot.SetViewportSize(new Vector2Int(width, height));
        uiRoot.Update();
        return uiRoot;
    }
}
