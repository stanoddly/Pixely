using System.Numerics;

namespace Pixely.Ui.Tests;

/// <summary>
/// The pointer arrives in window coordinates and the tree is laid out in the render target's, so
/// every hit test depends on the conversion between them being right at the edges as well as the
/// middle.
/// </summary>
public class PointerCoordinateTests
{
    [Test]
    public void AHalfScaledWindow_HalvesThePosition()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 600), new Vector2Int(400, 300));

        Assert.That(position, Is.EqualTo(new Vector2Int(100, 50)));
    }

    [Test]
    public void AWindowMatchingTheViewport_LeavesThePositionAlone()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(37, 11), new Size<uint>(320, 240), new Vector2Int(320, 240));

        Assert.That(position, Is.EqualTo(new Vector2Int(37, 11)));
    }

    [Test]
    public void APositionLeftOfTheOrigin_StaysOutside()
    {
        // Truncation would round -0.5 towards zero and put this inside the viewport, which turns a
        // release just past an element's left edge into a click on it.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(-1, -1), new Size<uint>(800, 600), new Vector2Int(400, 300));

        Assert.That(position, Is.EqualTo(new Vector2Int(-1, -1)));
    }

    [Test]
    public void AZeroExtentOnOneAxis_StillScalesTheOther()
    {
        // A minimised window reports zero. Abandoning both axes because one is unusable would put the
        // pointer somewhere it never was.
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 0), new Vector2Int(400, 300));

        Assert.That(position, Is.EqualTo(new Vector2Int(100, 100)));
    }

    [Test]
    public void NoViewportYet_PassesThePositionThrough()
    {
        Vector2Int position = UiInputSystem.ToUiPosition(new Vector2(200, 100), new Size<uint>(800, 600), new Vector2Int(0, 0));

        Assert.That(position, Is.EqualTo(new Vector2Int(200, 100)));
    }
}
