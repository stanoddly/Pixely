using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.MultiWindow;

public sealed class SecondaryWindowRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _vertexBuffer;

    ViewScope IRenderer<BasicRenderContext>.ViewScope => Program.SecondaryView;

    private SecondaryWindowRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionVertex> vertexBuffer)
    {
        _graphicsPipeline = graphicsPipeline;
        _vertexBuffer = vertexBuffer;
    }

    public void Render(BasicRenderContext renderContext)
    {
        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.Coral);
        using IRenderPass renderPass = renderContext.CommandBuffer.CreateRenderPass(renderContext.SwapchainTexture, ColorTargetSettings.Clear);

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);
        renderPass.DrawPrimitive();
    }

    public static SecondaryWindowRenderer Create(
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem)
    {
        GpuVertexBuffer<PositionVertex> vertexBuffer =
            gpuMemorySystem.CreateVertexBuffer(PositionShapes.VerticalQuad);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay(Program.SecondaryView)
            .Build();

        return new SecondaryWindowRenderer(graphicsPipeline, vertexBuffer);
    }
}
