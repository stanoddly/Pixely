using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.IndexedRenderPass;

public class IndexedRenderPassRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _indexedPipeline;
    private readonly GraphicsPipeline _instancedPipeline;
    private readonly GpuVertexBuffer<PositionColorVertex> _vertexBuffer;
    private readonly GpuIndexBuffer _indexBuffer;
    private readonly GpuStorageBuffer<Vector4> _instanceOffsets;
    private readonly GpuStorageBuffer<Vector4> _instanceTints;

    public IndexedRenderPassRenderer(
        GraphicsPipeline indexedPipeline,
        GraphicsPipeline instancedPipeline,
        GpuVertexBuffer<PositionColorVertex> vertexBuffer,
        GpuIndexBuffer indexBuffer,
        GpuStorageBuffer<Vector4> instanceOffsets,
        GpuStorageBuffer<Vector4> instanceTints)
    {
        _indexedPipeline = indexedPipeline;
        _instancedPipeline = instancedPipeline;
        _vertexBuffer = vertexBuffer;
        _indexBuffer = indexBuffer;
        _instanceOffsets = instanceOffsets;
        _instanceTints = instanceTints;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_indexedPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);
        renderPass.BindIndexBuffer(_indexBuffer);

        renderPass.DrawIndexedPrimitive(3, 0, 0);
        renderPass.DrawIndexedPrimitive(3, 3, 0);
        // Reuses the same six index values against the second quad's vertex range.
        renderPass.DrawIndexedPrimitive(6, 0, 4);

        renderPass.BindGraphicsPipeline(_instancedPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);
        renderPass.BindVertexStorageBuffer(_instanceOffsets);
        renderPass.BindFragmentStorageBuffer(_instanceTints);
        renderPass.DrawIndexedPrimitiveInstanced(6, 3, 0, 8, 0);
    }

    public static IndexedRenderPassRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem)
    {
        PositionColorVertex[] vertices =
        [
            new PositionColorVertex(new Vector3(-0.85f,  0.75f, 0.0f), Colors.Red),
            new PositionColorVertex(new Vector3(-0.85f,  0.20f, 0.0f), Colors.Green),
            new PositionColorVertex(new Vector3(-0.30f,  0.75f, 0.0f), Colors.Blue),
            new PositionColorVertex(new Vector3(-0.30f,  0.20f, 0.0f), Colors.Yellow),

            new PositionColorVertex(new Vector3( 0.30f,  0.75f, 0.0f), Colors.Cyan),
            new PositionColorVertex(new Vector3( 0.30f,  0.20f, 0.0f), Colors.Magenta),
            new PositionColorVertex(new Vector3( 0.85f,  0.75f, 0.0f), Colors.White),
            new PositionColorVertex(new Vector3( 0.85f,  0.20f, 0.0f), Colors.Orange),

            new PositionColorVertex(new Vector3(-0.17f,  0.17f, 0.0f), Colors.White),
            new PositionColorVertex(new Vector3(-0.17f, -0.17f, 0.0f), Colors.White),
            new PositionColorVertex(new Vector3( 0.17f,  0.17f, 0.0f), Colors.White),
            new PositionColorVertex(new Vector3( 0.17f, -0.17f, 0.0f), Colors.White)
        ];

        ushort[] indices =
        [
            0, 2, 1,
            2, 3, 1
        ];

        Vector4[] instanceOffsets =
        [
            new Vector4(-0.55f, -0.45f, 0.0f, 0.0f),
            new Vector4( 0.00f, -0.45f, 0.0f, 0.0f),
            new Vector4( 0.55f, -0.45f, 0.0f, 0.0f)
        ];

        Vector4[] instanceTints =
        [
            new Vector4(0.95f, 0.30f, 0.30f, 1.0f),
            new Vector4(0.25f, 0.80f, 0.95f, 1.0f),
            new Vector4(0.90f, 0.85f, 0.25f, 1.0f)
        ];

        GpuVertexBuffer<PositionColorVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer<PositionColorVertex>(vertices);
        GpuIndexBuffer indexBuffer = gpuMemorySystem.CreateIndexBuffer(indices);
        GpuStorageBuffer<Vector4> offsetBuffer = gpuMemorySystem.CreateStorageBuffer<Vector4>(instanceOffsets);
        GpuStorageBuffer<Vector4> tintBuffer = gpuMemorySystem.CreateStorageBuffer<Vector4>(instanceTints);

        GraphicsPipeline indexedPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleList)
            .AddVertexBufferConfig<PositionColorVertex>()
            .SetShaderProgram("shaders/indexed")
            .AddColorFormatFromDisplay()
            .Build();

        GraphicsPipeline instancedPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleList)
            .AddVertexBufferConfig<PositionColorVertex>()
            .SetShaderProgram("shaders/instanced")
            .AddColorFormatFromDisplay()
            .Build();

        return new IndexedRenderPassRenderer(
            indexedPipeline,
            instancedPipeline,
            vertexBuffer,
            indexBuffer,
            offsetBuffer,
            tintBuffer);
    }
}
