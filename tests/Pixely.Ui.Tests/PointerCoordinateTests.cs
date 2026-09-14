using System.Numerics;

namespace Pixely.Ui.Tests;

/// <summary>
/// The pointer arrives in window coordinates and the tree is laid out in logical pixels over the
/// render target, so every hit test depends on the conversion between them being right at the
/// edges as well as the middle.
/// </summary>
public class PointerCoordinateTests
{
    [Test]
    public void AHalfScaledWindow_HalvesThePosition()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 600), new Vector2Int(400, 300), 1f);

        Assert.That(position, Is.EqualTo(new Vector2Int(100, 50)));
    }

    [Test]
    public void AWindowMatchingTheViewport_LeavesThePositionAlone()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(37, 11), new Size<uint>(320, 240), new Vector2Int(320, 240), 1f);

        Assert.That(position, Is.EqualTo(new Vector2Int(37, 11)));
    }

    [Test]
    public void APositionLeftOfTheOrigin_StaysOutside()
    {
        // Truncation would round -0.5 towards zero and put this inside the viewport, which turns a
        // release just past an element's left edge into a click on it.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(-1, -1), new Size<uint>(800, 600), new Vector2Int(400, 300), 1f);

        Assert.That(position, Is.EqualTo(new Vector2Int(-1, -1)));
    }

    [Test]
    public void AZeroExtentOnOneAxis_StillScalesTheOther()
    {
        // A minimised window reports zero. Abandoning both axes because one is unusable would put the
        // pointer somewhere it never was.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 0), new Vector2Int(400, 300), 1f);

        Assert.That(position, Is.EqualTo(new Vector2Int(100, 100)));
    }

    [Test]
    public void NoViewportYet_PassesThePositionThrough()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 600), new Vector2Int(0, 0), 1f);

        Assert.That(position, Is.EqualTo(new Vector2Int(200, 100)));
    }

    [Test]
    public void AScaledRoot_DividesByTheScale()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(37, 11), new Size<uint>(640, 480), new Vector2Int(640, 480), 2f);

        Assert.That(position, Is.EqualTo(new Vector2Int(18, 5)));
    }

    [Test]
    public void AFractionalScale_FloorsOnceAtTheEnd()
    {
        // 2.9 target pixels is logical pixel 1 at 2.5x. Flooring to target pixel 2 first would put
        // it in logical pixel 0.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(2.9f, 0), new Size<uint>(640, 480), new Vector2Int(640, 480), 2.5f);

        Assert.That(position, Is.EqualTo(new Vector2Int(1, 0)));
    }

    [Test]
    public void ATargetTheScaleDoesNotDivide_ScalesAgainstTheTargetNotTheViewport()
    {
        // A 3 pixel target at 2x is laid out in 2 logical pixels. Scaling the window against those 2
        // rather than the 3 target pixels would put window pixel 1.5 in logical pixel 1.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(1.5f, 0), new Size<uint>(3, 3), new Vector2Int(3, 3), 2f);

        Assert.That(position, Is.EqualTo(new Vector2Int(0, 0)));
    }

    [Test]
    public void AHighDpiWindowAndAScale_ComposeInThatOrder()
    {
        // 200 window points on an 800 point window drawn into 1600 target pixels is target pixel 400,
        // which is logical pixel 100 at 4x.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 600), new Vector2Int(1600, 1200), 4f);

        Assert.That(position, Is.EqualTo(new Vector2Int(100, 50)));
    }

    [Test]
    public void NoTargetYet_StillDividesByTheScale()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 600), new Vector2Int(0, 0), 2f);

        Assert.That(position, Is.EqualTo(new Vector2Int(100, 50)));
    }
}
