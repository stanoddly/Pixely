using Pixely.Pencuil;

namespace Pixely.Tests;

public sealed class ScrollBarGeometryTests
{
    [Test]
    public void ClampOffset_KeepsOffsetWithinScrollableRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ScrollBarGeometry.ClampOffset(-10, 1000, 200), Is.Zero);
            Assert.That(ScrollBarGeometry.ClampOffset(500, 1000, 200), Is.EqualTo(500));
            Assert.That(ScrollBarGeometry.ClampOffset(5000, 1000, 200), Is.EqualTo(800));
        });
    }

    [Test]
    public void ClampOffset_ContentFittingViewportPinsToZero()
    {
        Assert.That(ScrollBarGeometry.ClampOffset(50, 100, 200), Is.Zero);
    }

    [Test]
    public void ThumbLength_IsProportionalToTheVisibleFraction()
    {
        Assert.That(ScrollBarGeometry.ThumbLength(400, 1000, 200, 16), Is.EqualTo(80));
    }

    [Test]
    public void ThumbLength_HonoursTheMinimumForVeryLongContent()
    {
        Assert.That(ScrollBarGeometry.ThumbLength(400, 1_000_000, 200, 16), Is.EqualTo(16));
    }

    [Test]
    public void ThumbLength_NonScrollableContentHasNoThumb()
    {
        Assert.That(ScrollBarGeometry.ThumbLength(400, 200, 200, 16), Is.Zero);
    }

    [Test]
    public void ThumbStart_SpansTheWholeTravelAcrossTheOffsetRange()
    {
        int thumbLength = ScrollBarGeometry.ThumbLength(400, 1000, 200, 16);

        Assert.Multiple(() =>
        {
            Assert.That(ScrollBarGeometry.ThumbStart(400, thumbLength, 0, 1000, 200), Is.Zero);
            Assert.That(ScrollBarGeometry.ThumbStart(400, thumbLength, 800, 1000, 200), Is.EqualTo(400 - thumbLength));
            Assert.That(ScrollBarGeometry.ThumbStart(400, thumbLength, 400, 1000, 200), Is.EqualTo((400 - thumbLength) / 2));
        });
    }

    [Test]
    public void OffsetFromThumbStart_InvertsThumbStart()
    {
        int thumbLength = ScrollBarGeometry.ThumbLength(400, 1000, 200, 16);

        Assert.Multiple(() =>
        {
            Assert.That(ScrollBarGeometry.OffsetFromThumbStart(400, thumbLength, 0, 1000, 200), Is.Zero);
            Assert.That(ScrollBarGeometry.OffsetFromThumbStart(400, thumbLength, 400 - thumbLength, 1000, 200), Is.EqualTo(800));
        });
    }

    [Test]
    public void OffsetFromThumbStart_ClampsDragsBeyondEitherEnd()
    {
        int thumbLength = ScrollBarGeometry.ThumbLength(400, 1000, 200, 16);

        Assert.Multiple(() =>
        {
            Assert.That(ScrollBarGeometry.OffsetFromThumbStart(400, thumbLength, -500, 1000, 200), Is.Zero);
            Assert.That(ScrollBarGeometry.OffsetFromThumbStart(400, thumbLength, 5000, 1000, 200), Is.EqualTo(800));
        });
    }

    [Test]
    public void OffsetFromThumbStart_RoundTripsEveryOffsetWithinAPixel()
    {
        int thumbLength = ScrollBarGeometry.ThumbLength(400, 1000, 200, 16);

        for (int offset = 0; offset <= 800; offset++)
        {
            int thumbStart = ScrollBarGeometry.ThumbStart(400, thumbLength, offset, 1000, 200);
            int roundTripped = ScrollBarGeometry.OffsetFromThumbStart(400, thumbLength, thumbStart, 1000, 200);

            Assert.That(roundTripped, Is.EqualTo(offset).Within(3), $"offset {offset} did not round-trip");
        }
    }
}
