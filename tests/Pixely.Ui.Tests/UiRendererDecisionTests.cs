using System.Numerics;
using Pixely.RenderOrchestration;

namespace Pixely.Ui.Tests;

/// <summary>
/// The decisions the renderer makes about instructions it did not build. Tested as pure functions
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
    public void InstructionsBuiltForATargetTheRootHasSinceLeft_AreStale()
    {
        Assert.That(UiRenderer<BasicRenderContext>.IsStale(_viewport, _otherViewport, _viewport), Is.True);
    }

    [Test]
    public void InstructionsBuiltForATargetThatIsNotTheOneDrawnInto_AreStale()
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

    [Test]
    public void AnUnscaledRoot_PresentsOverTheWholeTarget()
    {
        Matrix4x4 world = UiRenderer<BasicRenderContext>.CreatePresentWorld(_viewport, 1f, _viewport);

        Assert.That(world, Is.EqualTo(Matrix4x4.Identity));
    }

    [Test]
    public void AnIntegerScaleDividingTheTarget_PresentsOverTheWholeTarget()
    {
        Matrix4x4 world = UiRenderer<BasicRenderContext>.CreatePresentWorld(_viewport, 2f, _otherViewport);

        Assert.That(world, Is.EqualTo(Matrix4x4.Identity), "320 logical pixels at 2x are exactly the 640 target pixels");
    }

    [Test]
    public void AScaleNotDividingTheTarget_OverhangsByTheRoundedUpLogicalPixel()
    {
        // 3 target pixels at 2x lay out in 2 logical pixels, which present as 4 target pixels: the
        // quad covers 4/3 of the target and the target clips the rest.
        Matrix4x4 world = UiRenderer<BasicRenderContext>.CreatePresentWorld(new Vector2Int(2, 2), 2f, new Vector2Int(3, 3));

        Assert.Multiple(() =>
        {
            Assert.That(world.M11, Is.EqualTo(4f / 3f).Within(1e-6f));
            Assert.That(world.M22, Is.EqualTo(4f / 3f).Within(1e-6f));
            Assert.That(world.M41, Is.Zero, "logical pixel 0 stays on target pixel 0");
            Assert.That(world.M42, Is.Zero);
        });
    }
}
