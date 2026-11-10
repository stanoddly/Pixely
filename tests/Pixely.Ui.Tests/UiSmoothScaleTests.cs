using System.Numerics;
using Pixely.App;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Sprites;
using Pixely.Testing;
using Pixely.Text;

namespace Pixely.Ui.Tests;

/// <summary>
/// A root that lays out in logical pixels and paints in target ones. What matters is that the tree
/// stays in logical pixels, with text sized to what is drawn at the scale, while what is painted is
/// sharp at it: text rasterised at the scaled size and drawn one to one, edges on whole target
/// pixels, and pictures sampled smoothly.
/// </summary>
public class UiSmoothScaleTests
{
    private static readonly SrgbColor Red = new(255, 0, 0, 255);
    private static readonly SrgbColor Blue = new(0, 0, 255, 255);

    private StubGpuDevice _device = null!;

    [SetUp]
    public void SetUp()
    {
        _device = StubGpuDevice.Create();
    }

    [TearDown]
    public void TearDown()
    {
        _device.Dispose();
    }

    [Test]
    public void ALabel_IsRasterisedAtTheScaledSize_AndDrawnOneToOne()
    {
        using StubFontSystem fonts = new(glyphWidth: 8, lineHeight: 16);
        Label label = new(fonts.Load("fonts/body.ttf", 16), "abc") { Margin = new Thickness(3, 0, 0, 0) };

        UiRoot root = Build(1.33f, new Column { Children = { label } }, 400, 300);
        PaintInstruction text = root.Instructions.Single();

        Assert.Multiple(() =>
        {
            // 8 x 1.33 rounds to 11 and 16 x 1.33 to 21, which is the font at 1.33 times its size.
            Assert.That(text.Texture!.Size, Is.EqualTo(new ShortSize(33, 21)));
            // 3 x 1.33 is 3.99, and every texel lands on exactly one target pixel.
            Assert.That(text.Area, Is.EqualTo(new Rectangle(4, 0, 33, 21)));
            Assert.That(text.Filter, Is.EqualTo(TextureFilter.Nearest), "drawn one to one, so nothing to filter");
            // Laid out in logical pixels, at what is drawn: 33 / 1.33 and 21 / 1.33 rounded up.
            Assert.That(label.Bounds, Is.EqualTo(new Rectangle(3, 0, 25, 16)));
            Assert.That(Round((3 + 25) * 1.33) - 4, Is.GreaterThanOrEqualTo(text.Area.Width), "the bounds hold the text");
        });
    }

    [Test]
    public void ARequestedScale_RasterisesTheTextAgainAtIt()
    {
        using StubFontSystem fonts = new(glyphWidth: 8, lineHeight: 16);
        Label label = new(fonts.Load("fonts/body.ttf", 16), "abc");
        UiRoot root = Build(1.33f, new Column { Children = { label } }, 400, 300);

        root.RequestScale(2f);
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.Instructions.Single().Area, Is.EqualTo(new Rectangle(0, 0, 48, 32)));
            Assert.That(label.Bounds, Is.EqualTo(new Rectangle(0, 0, 24, 16)), "measured again in the font at the new scale");
        });
    }

    [Test]
    public void AFieldSizedToItsText_HoldsItWithoutScrolling()
    {
        using StubFontSystem fonts = new(glyphWidth: 8, lineHeight: 16);
        TextBox field = new(fonts.Load("fonts/body.ttf", 16)) { Text = "ab", Padding = new Thickness(0) };
        UiRoot root = Build(1.33f, new Column { Children = { field } }, 400, 300);

        PaintInstruction text = root.Instructions.Single(instruction => instruction.Texture != null);

        Assert.That(text.Clip.Width, Is.GreaterThanOrEqualTo(text.Area.Width), "the clip of its content holds the scaled text");
    }

    [Test]
    public void ALabelInAFontThatCannotScale_IsStretchedToItsLogicalSize()
    {
        RasterisingFont font = new();
        Label label = new(font, "ab");

        UiRoot root = Build(2f, new Column { Children = { label } }, 400, 300);
        PaintInstruction text = root.Instructions.Single();

        Assert.Multiple(() =>
        {
            Assert.That(text.Area, Is.EqualTo(new Rectangle(0, 0, 2 * RasterisingFont.CharacterWidth * 2, RasterisingFont.LineHeight * 2)));
            Assert.That(text.Filter, Is.EqualTo(TextureFilter.Linear), "stretched, so sampled the way the root samples sprites");
        });
    }

    [Test]
    public void TheTree_StaysInLogicalPixels()
    {
        RecordingPointerTarget target = new() { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20), Margin = new Thickness(50, 0, 0, 0) };
        Element fill = new() { Width = Sizing.Grow(), Height = Sizing.Grow() };

        UiRoot root = Build(1.5f, new Overlay { Children = { fill, target } }, 300, 150);
        root.PointerMoved(new Vector2Int(55, 5));

        Assert.Multiple(() =>
        {
            Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int(200, 100)));
            Assert.That(fill.Bounds, Is.EqualTo(new Rectangle(0, 0, 200, 100)));
            Assert.That(target.Calls, Is.EqualTo(new[] { "enter 55,5" }), "hit tested in logical pixels");
            Assert.That(root.PaintedRasterSize, Is.EqualTo(new Vector2Int(300, 150)), "painted at the target's size");
            Assert.That(root.PaintedPresentScale, Is.EqualTo(1f), "and presented one to one");
        });
    }

    [Test]
    public void APixelatedRoot_PaintsAtTheViewportAndPresentsAtTheScale()
    {
        UiRoot root = new() { Scale = 2f };
        root.AddLayer(new Column { Children = { new MeasuredBox(10, 10) { Background = new SolidDrawable(Red) } } });
        root.SetTargetSize(new Vector2Int(300, 150));
        root.Update();

        Assert.Multiple(() =>
        {
            Assert.That(root.PaintedRasterSize, Is.EqualTo(new Vector2Int(150, 75)));
            Assert.That(root.PaintedPresentScale, Is.EqualTo(2f));
            Assert.That(root.Instructions.Single().Area, Is.EqualTo(new Rectangle(0, 0, 10, 10)), "in logical pixels");
        });
    }

    [Test]
    public void AdjacentFills_MeetWithoutAGapOrAnOverlap()
    {
        Row row = new()
        {
            Children =
            {
                new MeasuredBox(10, 10) { Background = new SolidDrawable(Red) },
                new MeasuredBox(10, 10) { Background = new SolidDrawable(Blue) },
                new MeasuredBox(10, 10) { Background = new SolidDrawable(Red) }
            }
        };

        UiRoot root = Build(1.33f, row, 400, 300);
        Rectangle[] areas = root.Instructions.Select(instruction => instruction.Area).ToArray();

        // Each edge is rounded on its own: 0, 13.3, 26.6 and 39.9 land on 0, 13, 27 and 40.
        Assert.That(areas, Is.EqualTo(new[] { new Rectangle(0, 0, 13, 13), new Rectangle(13, 0, 14, 13), new Rectangle(27, 0, 13, 13) }));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ABorder_IsEquallyThickOnEverySide_AndItsFillMeetsIt(int offset)
    {
        MeasuredBox box = new(20, 20)
        {
            Margin = new Thickness(offset, offset, 0, 0),
            Background = new BorderDrawable(Red, new Thickness(1), new SolidDrawable(Blue))
        };

        UiRoot root = Build(1.33f, new Column { Children = { box } }, 400, 300);
        Rectangle[] edges = root.Instructions.Take(4).Select(instruction => instruction.Area).ToArray();
        Rectangle fill = root.Instructions[4].Area;
        Rectangle outer = new(Round(offset * 1.33), Round(offset * 1.33), Round((offset + 20) * 1.33) - Round(offset * 1.33), Round((offset + 20) * 1.33) - Round(offset * 1.33));

        Assert.Multiple(() =>
        {
            Assert.That(edges.Select(edge => Math.Min(edge.Width, edge.Height)), Is.All.EqualTo(1), "1 x 1.33 rounds to 1 wherever the border sits");
            Assert.That(fill, Is.EqualTo(new Rectangle(outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2)), "no gap and no overlap");
        });
    }

    [Test]
    public void ABorderInsideABordersFill_MeetsItAndItsOwnFill()
    {
        MeasuredBox box = new(20, 20)
        {
            Margin = new Thickness(1, 1, 0, 0),
            Background = new BorderDrawable(Red, new Thickness(1), new BorderDrawable(Blue, new Thickness(1), new SolidDrawable(Red)))
        };

        UiRoot root = Build(1.33f, new Column { Children = { box } }, 400, 300);
        Rectangle[] areas = root.Instructions.Select(instruction => instruction.Area).ToArray();
        Rectangle outer = new(Round(1.33), Round(1.33), Round(21 * 1.33) - Round(1.33), Round(21 * 1.33) - Round(1.33));
        Rectangle middle = new(outer.X + 1, outer.Y + 1, outer.Width - 2, outer.Height - 2);

        Assert.Multiple(() =>
        {
            Assert.That(areas, Has.Length.EqualTo(9));
            Assert.That(areas[4], Is.EqualTo(new Rectangle(middle.X, middle.Y, middle.Width, 1)), "the inner border starts where the outer one ends");
            Assert.That(areas[8], Is.EqualTo(new Rectangle(middle.X + 1, middle.Y + 1, middle.Width - 2, middle.Height - 2)), "and its fill where it ends");
        });
    }

    [Test]
    public void ANinePatchThatChoosesNearest_KeepsIt()
    {
        MeasuredBox box = new(20, 20) { Background = new NinePatchDrawable(CreateSprite(9, 9), new Thickness(3), SrgbColors.White, TextureFilter.Nearest) };

        UiRoot root = Build(1.33f, new Column { Children = { box } }, 400, 300);

        Assert.That(root.Instructions.Select(instruction => instruction.Filter), Is.All.EqualTo(TextureFilter.Nearest));
    }

    [Test]
    public void ANinePatch_KeepsItsCornersOneSize()
    {
        SpriteAsset sprite = CreateSprite(9, 9);
        MeasuredBox box = new(20, 20) { Margin = new Thickness(1, 1, 0, 0), Background = new NinePatchDrawable(sprite, new Thickness(3), SrgbColors.White) };

        UiRoot root = Build(1.33f, new Column { Children = { box } }, 400, 300);
        Rectangle[] areas = root.Instructions.Select(instruction => instruction.Area).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(areas, Has.Length.EqualTo(9));
            // 3 x 1.33 is 3.99, so each corner is 4 target pixels square.
            Assert.That(new[] { areas[0], areas[2], areas[6], areas[8] }.Select(corner => (corner.Width, corner.Height)), Is.All.EqualTo((4, 4)));
            Assert.That(areas[2].X + areas[2].Width, Is.EqualTo(Round(21 * 1.33)), "the patch still covers the element");
        });
    }

    [TestCase(2f)]
    [TestCase(0.5f)]
    public void AStretchedImage_IsSampledLinearly(float stretch)
    {
        Image image = new(CreateSprite(16, 16)) { Width = Sizing.Fixed((int)(16 * stretch)), Height = Sizing.Fixed((int)(16 * stretch)) };

        UiRoot root = Build(1.5f, new Column { Children = { image } }, 400, 300);

        Assert.That(root.Instructions.Single().Filter, Is.EqualTo(TextureFilter.Linear));
    }

    [Test]
    public void AnImageThatChoosesNearest_KeepsIt()
    {
        Image image = new(CreateSprite(16, 16)) { Filter = TextureFilter.Nearest };

        UiRoot root = Build(1.5f, new Column { Children = { image } }, 400, 300);

        Assert.That(root.Instructions.Single().Filter, Is.EqualTo(TextureFilter.Nearest));
    }

    [Test]
    public void APixelatedRoot_SamplesNearest_UnlessAnImageChoosesLinear()
    {
        Image pixelArt = new(CreateSprite(16, 16)) { Width = Sizing.Fixed(32), Height = Sizing.Fixed(32) };
        Image photo = new(CreateSprite(16, 16)) { Width = Sizing.Fixed(8), Height = Sizing.Fixed(8), Filter = TextureFilter.Linear };
        UiRoot root = new();
        root.AddLayer(new Column { Children = { pixelArt, photo } });
        root.SetTargetSize(new Vector2Int(100, 100));
        root.Update();

        Assert.That(root.Instructions.Select(instruction => instruction.Filter), Is.EqualTo(new[] { TextureFilter.Nearest, TextureFilter.Linear }));
    }

    [Test]
    public void AFilterChange_SplitsTheBatch()
    {
        SpriteAsset sprite = CreateSprite(16, 16);
        Column column = new() { Children = { new Image(sprite), new Image(sprite) { Filter = TextureFilter.Nearest } } };

        UiRoot root = Build(1.5f, column, 400, 300);

        Assert.That(root.Batches.Select(batch => batch.Filter), Is.EqualTo(new[] { TextureFilter.Linear, TextureFilter.Nearest }));
    }

    [Test]
    public void ATextBoxCaret_LandsAfterTheScaledText()
    {
        using StubFontSystem fonts = new(glyphWidth: 8, lineHeight: 16);
        TextBox field = new(fonts.Load("fonts/body.ttf", 16)) { Text = "ab", Width = Sizing.Fixed(100) };
        UiRoot root = Build(1.33f, new Column { Children = { field } }, 400, 300);

        root.Focus(field);
        root.Update();
        PaintInstruction text = root.Instructions.Single(instruction => instruction.Texture != null);
        PaintInstruction caret = root.Instructions[^1];

        Assert.Multiple(() =>
        {
            // The padding of 4 is 5.32 target pixels, and the text two 11 px glyphs.
            Assert.That(text.Area.X, Is.EqualTo(5));
            Assert.That(caret.Area, Is.EqualTo(new Rectangle(5 + 22, text.Area.Y, 1, caret.Area.Height)));
        });
    }

    [TestCase(0.5f)]
    [TestCase(0.75f)]
    public void ASmoothScaleBelowOne_IsAccepted(float scale)
    {
        UiRoot root = Build(scale, new Element { Width = Sizing.Grow(), Height = Sizing.Grow() }, 300, 150);

        Assert.That(root.ViewportSize, Is.EqualTo(new Vector2Int((int)Math.Ceiling(300 / scale), (int)Math.Ceiling(150 / scale))));
    }

    [Test]
    public void AScaleOutsideItsModesRange_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiRoot { Scale = 0.5f });
            Assert.Throws<ArgumentOutOfRangeException>(() => new UiRoot().RequestScale(0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new UiRoot(UiScaleMode.Smooth).RequestScale(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiRoot((UiScaleMode)7));
        });
    }

    [Test]
    public void TheRenderer_PaintsAtTheTargetSize_AndSamplesLinearImagesThroughTheFootprint()
    {
        PixelyAppBuilder builder = TestApp.CreateBuilder();
        builder.UseDefaultRendering(new WindowConfig(Size: (200, 100)));
        builder.UseUi(scale: 1.5f, scaleMode: UiScaleMode.Smooth);
        builder.OnBuilt(provider =>
        {
            Texture texture = provider.GetRequiredService<StubGpuDevice>().CreateTexture(new ShortSize(64, 64), TextureFormat.R8G8B8A8UnormSrgb, TextureUsage.Sampler);
            SpriteAsset sprite = new(texture, new ShortRectangle(0, 0, 64, 64));
            provider.GetUiRoot().AddLayer(new Column
            {
                Children =
                {
                    new Image(sprite) { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20) },
                    new Image(sprite) { Width = Sizing.Fixed(20), Height = Sizing.Fixed(20), Filter = TextureFilter.Nearest }
                }
            });
        });
        using IPixelyApp app = builder.Build();

        app.RunFrame();
        StubCommandBuffer frame = app.GetRequiredService<StubGpuDevice>().SubmittedCommandBuffers.Single(commandBuffer => commandBuffer.RenderPasses.Count > 0);
        StubDraw linear = frame.RenderPasses[0].Draws[0];
        StubDraw nearest = frame.RenderPasses[0].Draws[1];
        StubDraw present = frame.RenderPasses[^1].Draws.Single();

        Assert.Multiple(() =>
        {
            Assert.That(frame.RenderPasses[0].ColorTargets.Single().Size, Is.EqualTo(new ShortSize(200, 100)), "the retained texture is the target's size");
            Assert.That(linear.ReadFragmentUniform<Vector4>(2).X, Is.EqualTo(1f), "linear sampling averages the footprint");
            Assert.That(nearest.ReadFragmentUniform<Vector4>(2).X, Is.Zero, "nearest takes one sample");
            Assert.That(nearest.FragmentSamplers[0].Sampler, Is.SameAs(present.FragmentSamplers[0].Sampler), "the nearest image and the present share the nearest sampler");
            Assert.That(linear.FragmentSamplers[0].Sampler, Is.Not.SameAs(nearest.FragmentSamplers[0].Sampler), "and the linear image has the other");
            Assert.That(present.ReadVertexUniform<Matrix4x4>(1), Is.EqualTo(Matrix4x4.Identity), "presented one to one");
        });
    }

    private static UiRoot Build(float scale, Element layer, int width, int height)
    {
        UiRoot root = new(UiScaleMode.Smooth) { Scale = scale };
        root.AddLayer(layer);
        root.SetTargetSize(new Vector2Int(width, height));
        root.Update();
        return root;
    }

    private static int Round(double value) => (int)Math.Floor(value + 0.5);

    // A stub device's texture: a paint pass records it without dereferencing anything.
    private SpriteAsset CreateSprite(int width, int height) =>
        new(_device.CreateTexture(new ShortSize((ushort)width, (ushort)height), TextureFormat.R8G8B8A8Unorm, TextureUsage.Sampler), new ShortRectangle(0, 0, (ushort)width, (ushort)height));
}
