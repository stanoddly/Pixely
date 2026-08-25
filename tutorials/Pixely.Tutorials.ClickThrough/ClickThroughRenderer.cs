using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.ClickThrough;

public class ClickThroughRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _quad;

    public ClickThroughRenderer(GraphicsPipeline graphicsPipeline, GpuVertexBuffer<PositionVertex> quad)
    {
        _graphicsPipeline = graphicsPipeline;
        _quad = quad;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(new ColorTargetSettings
            {
                ClearColorValue = FColors.Black,
                LoadOperation = LoadOperation.Clear
            })
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.SkyBlue);
        renderPass.BindVertexBuffer(_quad);
        renderPass.DrawPrimitive();
    }

    public static ClickThroughRenderer Create(ShaderLoader shaderLoader, GraphicsPipelineBuilder graphicsPipelineBuilder, GpuMemorySystem gpuMemorySystem)
    {
        // NDC (-0.75, -0.75) to (0.75, 0.75) maps to pixels (50, 50)-(350, 350) in a 400x400 window
        ReadOnlySpan<PositionVertex> vertices =
        [
            new(new Vector3(-0.75f, -0.75f, 0.0f)),
            new(new Vector3(-0.75f,  0.75f, 0.0f)),
            new(new Vector3( 0.75f, -0.75f, 0.0f)),
            new(new Vector3( 0.75f,  0.75f, 0.0f)),
        ];

        GpuVertexBuffer<PositionVertex> quad = gpuMemorySystem.CreateVertexBuffer(vertices);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .Build();

        return new ClickThroughRenderer(graphicsPipeline, quad);
    }
}
