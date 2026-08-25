using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.StencilBuffer;

public class StencilBufferRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _maskPipeline;
    private readonly GraphicsPipeline _drawPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _smallQuadBuffer;
    private readonly GpuVertexBuffer<PositionVertex> _fullScreenQuadBuffer;
    private readonly Texture _depthStencilTexture;

    public StencilBufferRenderer(
        GraphicsPipeline maskPipeline,
        GraphicsPipeline drawPipeline,
        GpuVertexBuffer<PositionVertex> smallQuadBuffer,
        GpuVertexBuffer<PositionVertex> fullScreenQuadBuffer,
        Texture depthStencilTexture)
    {
        _maskPipeline = maskPipeline;
        _drawPipeline = drawPipeline;
        _smallQuadBuffer = smallQuadBuffer;
        _fullScreenQuadBuffer = fullScreenQuadBuffer;
        _depthStencilTexture = depthStencilTexture;
    }

    public void Render(BasicRenderContext renderContext)
    {
        using IRenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture, new ColorTargetSettings
            {
                ClearColorValue = FColors.Black
            })
            .SetDepthBuffer(_depthStencilTexture, new DepthBufferSettings
            {
                StencilLoadOperation = LoadOperation.Clear,
                StencilStoreOperation = StoreOperation.Store,
                ClearStencilValue = 0,
                DepthBufferLoadOperation = LoadOperation.Clear,
                DepthBufferStoreOperation = StoreOperation.DontCare
            })
            .Build();

        // Draw 1: Write stencil mask with the small quad (magenta, but color write could be off — we keep it to show the mask area)
        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.Magenta);
        renderPass.BindGraphicsPipeline(_maskPipeline);
        renderPass.SetStencilReference(1);
        renderPass.BindVertexBuffer(_smallQuadBuffer);
        renderPass.DrawPrimitive();

        // Draw 2: Draw full-screen quad, but only where stencil == 1 (the small quad area)
        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.Cyan);
        renderPass.BindGraphicsPipeline(_drawPipeline);
        renderPass.SetStencilReference(1);
        renderPass.BindVertexBuffer(_fullScreenQuadBuffer);
        renderPass.DrawPrimitive();
    }

    public static StencilBufferRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        GpuDevice gpuDevice)
    {
        Texture depthStencilTexture = gpuDevice.CreateDepthBufferTexture(
            new ShortSize(1280, 720),
            DepthBufferFormat.Depth32Stencil8);

        // Small centered quad (half size)
        ReadOnlySpan<PositionVertex> smallQuad =
        [
            new(new Vector3(-0.5f, -0.5f, 0.0f)),
            new(new Vector3(-0.5f, 0.5f, 0.0f)),
            new(new Vector3(0.5f, -0.5f, 0.0f)),
            new(new Vector3(0.5f, 0.5f, 0.0f)),
        ];
        GpuVertexBuffer<PositionVertex> smallQuadBuffer = gpuMemorySystem.CreateVertexBuffer(smallQuad);

        // Full-screen quad
        GpuVertexBuffer<PositionVertex> fullScreenQuadBuffer = gpuMemorySystem.CreateVertexBuffer(PositionShapes.VerticalQuad);

        var maskStencilState = new StencilOperationState(
            Fail: StencilOperation.Keep,
            Pass: StencilOperation.Replace,
            DepthFail: StencilOperation.Keep,
            Compare: CompareOperation.Always);

        GraphicsPipeline maskPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .EnableDepthTesting(DepthBufferFormat.Depth32Stencil8, write: false, compareOp: CompareOperation.Always)
            .EnableStencilTesting(maskStencilState, CompareOperation.Always)
            .Build();

        var drawStencilState = new StencilOperationState(
            Fail: StencilOperation.Keep,
            Pass: StencilOperation.Keep,
            DepthFail: StencilOperation.Keep,
            Compare: CompareOperation.Equal);

        GraphicsPipeline drawPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .EnableDepthTesting(DepthBufferFormat.Depth32Stencil8, write: false, compareOp: CompareOperation.Always)
            .EnableStencilTesting(drawStencilState, CompareOperation.Equal)
            .Build();

        return new StencilBufferRenderer(maskPipeline, drawPipeline, smallQuadBuffer, fullScreenQuadBuffer, depthStencilTexture);
    }
}
