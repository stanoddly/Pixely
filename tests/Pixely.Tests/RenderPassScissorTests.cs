using Pixely.Gpu;

namespace Pixely.Tests;

public class RenderPassScissorTests
{
    private static readonly ShortSize TargetSize = new(800, 600);

    [Test]
    public void ValidateScissorBounds_WithRectangleInsideTarget_Succeeds()
    {
        Assert.DoesNotThrow(() => RenderPassValidator.ValidateScissorBounds(new Rectangle(10, 20, 100, 50), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_WithRectangleCoveringWholeTarget_Succeeds()
    {
        Assert.DoesNotThrow(() => RenderPassValidator.ValidateScissorBounds(new Rectangle(0, 0, 800, 600), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_WithEmptyRectangle_Succeeds()
    {
        Assert.DoesNotThrow(() => RenderPassValidator.ValidateScissorBounds(new Rectangle(400, 300, 0, 0), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_WithNegativeWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(10, 10, -1, 50), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_WithNegativeHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(10, 10, 50, -1), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_WithNegativeOrigin_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(-1, 0, 10, 10), TargetSize));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(0, -1, 10, 10), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_ExceedingTargetWidthByOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(700, 0, 101, 10), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_ExceedingTargetHeightByOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(0, 500, 10, 101), TargetSize));
    }

    [Test]
    public void ValidateScissorBounds_WithOverflowingExtent_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RenderPassValidator.ValidateScissorBounds(new Rectangle(1, 0, int.MaxValue, 10), TargetSize));
    }
}
