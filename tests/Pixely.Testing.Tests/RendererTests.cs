using System.Numerics;
using Pixely.App;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.ShaderCommon;

namespace Pixely.Testing.Tests;

// What a game's renderer test looks like: the game's real registrations over the stub device, one frame, assertions on what
// the renderer recorded.
public class RendererTests
{
    private static readonly ShortSize WindowSize = new(320, 200);

    [Test]
    public void RunFrame_RecordsTheRenderersDraw()
    {
        using IPixelyApp app = BuildApp();
        SpriteRenderer renderer = GetRenderer(app);
        renderer.Camera = Matrix4x4.CreateOrthographicOffCenter(0, 320, 200, 0, -1, 1);
        renderer.SpriteCount = 3;

        app.RunFrame();

        StubRenderPass renderPass = GetFrame(app).RenderPasses.Single();
        StubDraw draw = renderPass.Draws.Single();
        Assert.Multiple(() =>
        {
            Assert.That(renderPass.ColorTargets.Single().Size, Is.EqualTo(WindowSize));
            Assert.That(renderPass.ColorTargetSettings.Single().LoadOperation, Is.EqualTo(LoadOperation.Clear));
            Assert.That(draw.Pipeline, Is.SameAs(renderer.Pipeline));
            Assert.That(draw.VertexBuffers[0], Is.SameAs(renderer.Quad));
            Assert.That(draw.FragmentSamplers[0], Is.EqualTo(new StubSamplerBinding(renderer.Atlas, renderer.Sampler)));
            Assert.That(draw.ReadVertexUniform<Matrix4x4>(0), Is.EqualTo(renderer.Camera));
            Assert.That(draw.IsIndexed, Is.False);
            Assert.That(draw.VertexCount, Is.EqualTo(4));
            Assert.That(draw.InstanceCount, Is.EqualTo(3));
            Assert.That(draw.Scissor, Is.EqualTo(new Rectangle(10, 10, 310, 190)));
        });
    }

    [Test]
    public void RunFrame_SubmitsTheRenderersUploadsBeforeTheFrame()
    {
        using IPixelyApp app = BuildApp();
        SpriteRenderer renderer = GetRenderer(app);
        StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();

        app.RunFrame();

        Assert.Multiple(() =>
        {
            Assert.That(device.ReadVertexBuffer(renderer.Quad), Is.EqualTo(SpriteRenderer.QuadVertices));
            Assert.That(device.ReadTexture(renderer.Atlas), Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        });
    }

    [Test]
    public void RunFrame_WithoutAUniformTheShaderNeeds_FailsLikeOnTheGpu()
    {
        using IPixelyApp app = BuildApp();
        GetRenderer(app).PushesCamera = false;

        Assert.Throws<ShaderBindingLayoutValidationException>(() => app.RunFrame());
    }

    [Test]
    public void Render_WithAContextOfTheTestsOwn_RecordsIntoItsCommandBuffer()
    {
        using IPixelyApp app = BuildApp();
        SpriteRenderer renderer = GetRenderer(app);
        StubGpuDevice device = app.GetRequiredService<StubGpuDevice>();
        StubCommandBuffer commandBuffer = (StubCommandBuffer)device.AcquireCommandBuffer();

        renderer.Render(new BasicRenderContext(device.CreateSwapchainTexture(new ShortSize(64, 64)), commandBuffer));
        commandBuffer.Submit();

        Assert.That(commandBuffer.RenderPasses.Single().Draws.Single().Scissor, Is.EqualTo(new Rectangle(10, 10, 54, 54)));
    }

    private static IPixelyApp BuildApp()
    {
        PixelyAppBuilder builder = new();
        builder.UseDefaultContent();
        builder.UseStubGpu();
        builder.UseDefaultRendering(new WindowConfig(Size: (WindowSize.Width, WindowSize.Height)));
        builder.AddSingleton<IRenderer<BasicRenderContext>, SpriteRenderer>();
        return builder.Build();
    }

    private static SpriteRenderer GetRenderer(IPixelyApp app)
    {
        return (SpriteRenderer)app.GetRequiredService<IRenderer<BasicRenderContext>>();
    }

    // The frame is the command buffer with render passes; the others carry uploads or an offscreen window's wait.
    private static StubCommandBuffer GetFrame(IPixelyApp app)
    {
        return app.GetRequiredService<StubGpuDevice>().SubmittedCommandBuffers.Single(commandBuffer => commandBuffer.RenderPasses.Count > 0);
    }
}
