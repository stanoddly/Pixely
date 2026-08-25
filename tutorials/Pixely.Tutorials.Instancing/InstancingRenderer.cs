using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.Instancing;

public class InstancingRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _quadVertexBuffer;
    private readonly GpuStorageBuffer<Vector4> _offsetBuffer;
    private readonly GpuStorageBuffer<Vector4> _colorBuffer;

    public InstancingRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionVertex> quadVertexBuffer,
        GpuStorageBuffer<Vector4> offsetBuffer,
        GpuStorageBuffer<Vector4> colorBuffer)
    {
        _graphicsPipeline = graphicsPipeline;
        _quadVertexBuffer = quadVertexBuffer;
        _offsetBuffer = offsetBuffer;
        _colorBuffer = colorBuffer;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_quadVertexBuffer);
        renderPass.BindVertexStorageBuffer(_offsetBuffer);
        renderPass.BindFragmentStorageBuffer(_colorBuffer);

        renderPass.DrawPrimitiveInstanced(4);
    }

    public static InstancingRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        GpuDevice gpuDevice)
    {
        // Position offsets for 2x2 grid (xy = offset, zw = padding)
        Vector4[] offsets =
        [
            new Vector4(-0.5f,  0.5f, 0.0f, 0.0f), // Top-left
            new Vector4( 0.5f,  0.5f, 0.0f, 0.0f), // Top-right
            new Vector4(-0.5f, -0.5f, 0.0f, 0.0f), // Bottom-left
            new Vector4( 0.5f, -0.5f, 0.0f, 0.0f), // Bottom-right
        ];

        // Colors for each instance
        Vector4[] colors =
        [
            new Vector4(1.0f, 0.0f, 0.0f, 1.0f), // Red
            new Vector4(0.0f, 1.0f, 0.0f, 1.0f), // Green
            new Vector4(0.0f, 0.0f, 1.0f, 1.0f), // Blue
            new Vector4(1.0f, 1.0f, 0.0f, 1.0f), // Yellow
        ];

        GpuStorageBuffer<Vector4> offsetBuffer = gpuMemorySystem.CreateStorageBuffer<Vector4>(offsets);
        GpuStorageBuffer<Vector4> colorBuffer = gpuMemorySystem.CreateStorageBuffer<Vector4>(colors);

        GpuVertexBuffer<PositionVertex> quadVertexBuffer =
            gpuMemorySystem.CreateVertexBuffer(PositionShapes.VerticalQuad);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .Build();

        return new InstancingRenderer(graphicsPipeline, quadVertexBuffer, offsetBuffer, colorBuffer);
    }
}
