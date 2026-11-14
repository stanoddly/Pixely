namespace Pixely.Tests;

public sealed class WindowChangeDetectionTests
{
    [Test]
    public void OnScaleChanged_WithTheStartingValues_RaisesNothing()
    {
        FixedWindow window = new(default, new ShortSize(640, 480), pixelDensity: 1, displayScale: 1);
        List<ScaleChangedEventArgs> changes = [];
        window.ScaleChanged += changes.Add;

        window.OnPixelSizeChanged(0);
        window.OnScaleChanged(0);

        Assert.That(changes, Is.Empty);
    }

    [Test]
    public void OnScaleChanged_AfterTheScaleChanged_RaisesItOnce()
    {
        FixedWindow window = new(default, new ShortSize(640, 480), pixelDensity: 1, displayScale: 1);
        List<ScaleChangedEventArgs> changes = [];
        window.ScaleChanged += changes.Add;
        window.Scale = 2;

        window.OnScaleChanged(5);
        window.OnScaleChanged(6);

        Assert.That(changes, Is.EqualTo(new[] { new ScaleChangedEventArgs(1, 2, 5) }));
    }

    [Test]
    public void OnPixelSizeChanged_AfterTheSizeChanged_RaisesItOnce()
    {
        FixedWindow window = new(default, new ShortSize(640, 480));
        List<ResolutionChangedEventArgs> changes = [];
        window.ResolutionChanged += changes.Add;
        window.RenderSize = new ShortSize(800, 600);

        window.OnPixelSizeChanged(5);
        window.OnPixelSizeChanged(6);

        Assert.That(changes, Is.EqualTo(new[] { new ResolutionChangedEventArgs(new ShortSize(640, 480), new ShortSize(800, 600), 5) }));
    }
}
