using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.TransparentWindow;

public class TransparentWindowRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _topLeftQuad;
    private readonly GpuVertexBuffer<PositionVertex> _bottomRightQuad;

    public TransparentWindowRenderer(GraphicsPipeline graphicsPipeline, GpuVertexBuffer<PositionVertex> topLeftQuad, GpuVertexBuffer<PositionVertex> bottomRightQuad)
    {
        _graphicsPipeline = graphicsPipeline;
        _topLeftQuad = topLeftQuad;
        _bottomRightQuad = bottomRightQuad;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(new ColorTargetSettings
            {
                ClearColorValue = FColors.Transparent,
                LoadOperation = LoadOperation.Clear
            })
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);

        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.Magenta);
        renderPass.BindVertexBuffer(_topLeftQuad);
        renderPass.DrawPrimitive();

        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.Cyan);
        renderPass.BindVertexBuffer(_bottomRightQuad);
        renderPass.DrawPrimitive();
    }

    public static TransparentWindowRenderer Create(ShaderLoader shaderLoader, GraphicsPipelineBuilder graphicsPipelineBuilder, GpuMemorySystem gpuMemorySystem)
    {
        ReadOnlySpan<PositionVertex> topLeftVertices =
        [
            new(new Vector3(-0.9f, -0.9f, 0.0f)),
            new(new Vector3(-0.9f, 0.1f, 0.0f)),
            new(new Vector3(-0.1f, -0.9f, 0.0f)),
            new(new Vector3(-0.1f, 0.1f, 0.0f)),
        ];

        ReadOnlySpan<PositionVertex> bottomRightVertices =
        [
            new(new Vector3(0.1f, -0.1f, 0.0f)),
            new(new Vector3(0.1f, 0.9f, 0.0f)),
            new(new Vector3(0.9f, -0.1f, 0.0f)),
            new(new Vector3(0.9f, 0.9f, 0.0f)),
        ];

        GpuVertexBuffer<PositionVertex> topLeftQuad = gpuMemorySystem.CreateVertexBuffer(topLeftVertices);
        GpuVertexBuffer<PositionVertex> bottomRightQuad = gpuMemorySystem.CreateVertexBuffer(bottomRightVertices);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .Build();

        return new TransparentWindowRenderer(graphicsPipeline, topLeftQuad, bottomRightQuad);
    }
}
