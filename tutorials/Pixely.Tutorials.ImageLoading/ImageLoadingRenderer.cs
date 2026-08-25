using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.ImageLoading;

public class ImageLoadingRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionTextureVertex> _quadVertexBuffer;
    private readonly Texture _texture;
    private readonly Sampler _sampler;

    public ImageLoadingRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionTextureVertex> quadVertexBuffer,
        Texture texture,
        Sampler sampler)
    {
        _graphicsPipeline = graphicsPipeline;
        _quadVertexBuffer = quadVertexBuffer;
        _texture = texture;
        _sampler = sampler;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_quadVertexBuffer);
        renderPass.BindFragmentSampler(_texture, _sampler);

        renderPass.DrawPrimitive();
    }

    public static ImageLoadingRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        GpuDevice gpuDevice,
        ITextureLoader textureLoader)
    {
        // Load image from file using SDL3_image
        Texture texture = textureLoader.Load("images/sample.png");

        // Create vertex buffer with a quad
        GpuVertexBuffer<PositionTextureVertex> quadVertexBuffer =
            gpuMemorySystem.CreateVertexBuffer(PositionTextureShapes.VerticalQuad);

        // Create sampler for texture filtering
        Sampler sampler = gpuDevice.CreateSampler(SamplerConfig.Linear);

        // Build graphics pipeline with premultiplied alpha blending
        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionTextureVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay(
                BlendingState.PremultipliedAlpha)
            .Build();

        return new ImageLoadingRenderer(graphicsPipeline, quadVertexBuffer, texture, sampler);
    }
}
