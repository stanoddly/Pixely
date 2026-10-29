using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;

namespace Pixely.Testing.Tests;

// A game's renderer as the tests drive it: it builds its pipeline from the fixture shader in ShaderFixture, uploads a quad and
// a texture, and draws instanced quads through a camera matrix in vertex uniform slot 0 and the texture in fragment sampler 0.
public sealed class SpriteRenderer : IRenderer<BasicRenderContext>, IDisposable
{
    public static readonly PositionTextureVertex[] QuadVertices =
    [
        new(new Vector3(0, 0, 0), new Vector2(0, 0)),
        new(new Vector3(1, 0, 0), new Vector2(1, 0)),
        new(new Vector3(0, 1, 0), new Vector2(0, 1)),
        new(new Vector3(1, 1, 0), new Vector2(1, 1))
    ];

    public SpriteRenderer(GraphicsPipelineBuilder graphicsPipelineBuilder, GpuMemorySystem gpuMemorySystem, GpuDevice gpuDevice)
    {
        Pipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionTextureVertex>()
            .SetShaderProgram("shaders/sprite")
            .AddColorFormatFromDisplay()
            .Build();
        Quad = gpuMemorySystem.CreateVertexBuffer<PositionTextureVertex>(QuadVertices);
        Atlas = gpuMemorySystem.CreateTexture(new RawImage([255, 0, 0, 255], new ShortSize(1, 1), PixelFormat.Rgba8888));
        Sampler = gpuDevice.CreateSampler(SamplerConfig.PixelArt);
    }

    public GraphicsPipeline Pipeline { get; }

    public GpuVertexBuffer<PositionTextureVertex> Quad { get; }

    public Texture Atlas { get; }

    public Sampler Sampler { get; }

    public Matrix4x4 Camera { get; set; } = Matrix4x4.Identity;

    public uint SpriteCount { get; set; } = 1;

    // Cleared by a test to leave the camera out, which the shader's layout requires.
    public bool PushesCamera { get; set; } = true;

    public void Render(BasicRenderContext renderContext)
    {
        if (PushesCamera)
        {
            renderContext.CommandBuffer.PushVertexUniformData(0, Camera);
        }

        using RenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(Pipeline);
        renderPass.BindVertexBuffer(Quad);
        renderPass.BindFragmentSampler(Atlas, Sampler);
        renderPass.SetScissor(new Rectangle(10, 10, 1000, 1000));
        renderPass.DrawPrimitiveInstanced(SpriteCount);
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        Quad.Dispose();
        Atlas.Dispose();
        Sampler.Dispose();
    }
}
