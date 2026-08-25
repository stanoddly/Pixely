using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.IndexBuffer;

public class IndexBufferRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionColorVertex> _vertexBuffer;
    private readonly GpuIndexBuffer _indexBuffer;

    public IndexBufferRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionColorVertex> vertexBuffer,
        GpuIndexBuffer indexBuffer)
    {
        _graphicsPipeline = graphicsPipeline;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);
        renderPass.BindIndexBuffer(_indexBuffer);

        renderPass.DrawIndexedPrimitive();
    }

    public static IndexBufferRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem)
    {
        PositionColorVertex[] vertices =
        [
            new PositionColorVertex(new Vector3(-0.6f, -0.6f, 0.0f), Colors.Red),
            new PositionColorVertex(new Vector3(-0.6f,  0.6f, 0.0f), Colors.Green),
            new PositionColorVertex(new Vector3( 0.6f, -0.6f, 0.0f), Colors.Blue),
            new PositionColorVertex(new Vector3( 0.6f,  0.6f, 0.0f), Colors.Yellow)
        ];

        ushort[] indices =
        [
            0, 1, 2,
            2, 1, 3
        ];

        GpuVertexBuffer<PositionColorVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer<PositionColorVertex>(vertices);
        GpuIndexBuffer indexBuffer = gpuMemorySystem.CreateIndexBuffer(indices);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleList)
            .AddVertexBufferConfig<PositionColorVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .Build();

        return new IndexBufferRenderer(graphicsPipeline, vertexBuffer, indexBuffer);
    }
}
