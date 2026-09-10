using Pixely.RenderOrchestration;

namespace Pixely.Ui.Tests;

/// <summary>
/// The two decisions the renderer makes about instructions it did not build. Tested as predicates
/// because the renderer itself needs a GPU; what they do not cover is that the renderer never
/// starts a build, which <see cref="IUiPaintSource"/> makes a compile-time fact instead.
/// </summary>
public class UiRendererDecisionTests
{
    private static readonly Vector2Int _viewport = new(320, 240);
    private static readonly Vector2Int _otherViewport = new(640, 480);

    [Test]
    public void InstructionsBuiltForTheTargetBeingDrawnInto_AreCurrent()
    {
        Assert.That(UiRenderer<BasicRenderContext>.IsStale(_viewport, _viewport, _viewport), Is.False);
    }

    [Test]
    public void InstructionsBuiltForAViewportTheRootHasSinceLeft_AreStale()
    {
        Assert.That(UiRenderer<BasicRenderContext>.IsStale(_viewport, _otherViewport, _viewport), Is.True);
    }

    [Test]
    public void InstructionsBuiltForAViewportThatIsNotTheTarget_AreStale()
    {
        Assert.That(UiRenderer<BasicRenderContext>.IsStale(_viewport, _viewport, _otherViewport), Is.True);
    }

    [Test]
    public void ABuildThisRendererMissed_StillRepaints()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UiRenderer<BasicRenderContext>.NeedsRepaint(7, 5, false), Is.True, "two builds behind, not one");
            Assert.That(UiRenderer<BasicRenderContext>.NeedsRepaint(5, 5, false), Is.False);
            Assert.That(UiRenderer<BasicRenderContext>.NeedsRepaint(5, 5, true), Is.True, "a resized texture holds nothing yet");
            Assert.That(UiRenderer<BasicRenderContext>.NeedsRepaint(0, 0, true), Is.True, "which is what paints the first frame");
        });
    }
}
