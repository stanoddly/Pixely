using Pixely.Gpu;

namespace Pixely.Tests;

public class RenderPassScissorTests
{
    [Test]
    public void ValidateScissorSize_WithRectangleInsideTarget_Succeeds()
    {
        Assert.DoesNotThrow(() => RenderPassValidator.ValidateScissorSize(new Rectangle(10, 20, 100, 50)));
    }

    [Test]
    public void ValidateScissorSize_WithEmptyRectangle_Succeeds()
    {
        Assert.DoesNotThrow(() => RenderPassValidator.ValidateScissorSize(new Rectangle(400, 300, 0, 0)));
    }

    [Test]
    public void ValidateScissorSize_WithRectangleOutsideTarget_Succeeds()
    {
        Assert.DoesNotThrow(() => RenderPassValidator.ValidateScissorSize(new Rectangle(-10, -10, 10000, 10000)));
    }

    [Test]
    public void ValidateScissorSize_WithNegativeWidth_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderPassValidator.ValidateScissorSize(new Rectangle(10, 10, -1, 50)));
    }

    [Test]
    public void ValidateScissorSize_WithNegativeHeight_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RenderPassValidator.ValidateScissorSize(new Rectangle(10, 10, 50, -1)));
    }
}
