using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.TextureArray;

public class TextureArrayRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionTextureVertex> _quadVertexBuffer;
    private readonly Gpu.TextureArray _textureArray;
    private readonly Sampler _sampler;
    private float _time;

    public TextureArrayRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionTextureVertex> quadVertexBuffer,
        Gpu.TextureArray textureArray,
        Sampler sampler)
    {
        _graphicsPipeline = graphicsPipeline;
        _quadVertexBuffer = quadVertexBuffer;
        _textureArray = textureArray;
        _sampler = sampler;
    }

    public void Render(BasicRenderContext renderContext)
    {
        _time += 0.16f; // Approximate 60fps timestep

        // Cycle through layers every 1 second
        float layerIndex = (int)(_time) % _textureArray.LayerCount;

        renderContext.CommandBuffer.PushFragmentUniformData(0, layerIndex);

        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_quadVertexBuffer);
        renderPass.BindFragmentSamplerArray(_textureArray, _sampler);

        renderPass.DrawPrimitive();
    }

    public static TextureArrayRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        GpuDevice gpuDevice)
    {
        // Create solid color images programmatically
        Image[] images =
        [
            CreateSolidColorImage(255, 255, 0, 255),   // Yellow
            CreateSolidColorImage(255, 0, 0, 255),     // Red
            CreateSolidColorImage(0, 255, 0, 255),     // Green
            CreateSolidColorImage(0, 0, 255, 255),     // Blue
        ];

        // Create texture array from images
        Gpu.TextureArray textureArray = gpuMemorySystem.CreateTextureArray(images);

        // Create vertex buffer with a quad
        GpuVertexBuffer<PositionTextureVertex> quadVertexBuffer =
            gpuMemorySystem.CreateVertexBuffer(PositionTextureShapes.VerticalQuad);

        // Create sampler
        Sampler sampler = gpuDevice.CreateSampler(SamplerConfig.PixelArt);

        // Build graphics pipeline
        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionTextureVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .Build();

        return new TextureArrayRenderer(graphicsPipeline, quadVertexBuffer, textureArray, sampler);
    }

    private static RawImage CreateSolidColorImage(byte r, byte g, byte b, byte a)
    {
        const ushort size = 64;
        byte[] data = new byte[size * size * 4];

        for (int i = 0; i < size * size; i++)
        {
            data[i * 4 + 0] = r;
            data[i * 4 + 1] = g;
            data[i * 4 + 2] = b;
            data[i * 4 + 3] = a;
        }

        return new RawImage(data, (size, size), PixelFormat.Rgba8888);
    }
}
