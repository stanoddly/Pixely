using System.Numerics;
using Pixely.Gpu;
using Pixely.RenderOrchestration;

namespace Pixely.Tutorials.Multisampling;

public class MultisamplingRenderer : IRenderer<BasicRenderContext>
{
    // WebGPU supports only one and four samples.
    private const SampleCount Samples = SampleCount.Count4;
    private const DepthBufferFormat DepthFormat = DepthBufferFormat.Depth16;

    // The resolve writes the swapchain texture, so the multisampled contents are not kept.
    private static readonly ColorTargetSettings _resolveSettings = new() { StoreOperation = StoreOperation.Resolve };
    private static readonly DepthBufferSettings _depthSettings = new() { DepthBufferStoreOperation = StoreOperation.DontCare };

    private readonly GpuDevice _gpuDevice;
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _triangleVertexBuffer;
    private Texture? _colorTarget;
    private Texture? _depthBuffer;

    public MultisamplingRenderer(GpuDevice gpuDevice, GraphicsPipeline graphicsPipeline, GpuVertexBuffer<PositionVertex> triangleVertexBuffer)
    {
        _gpuDevice = gpuDevice;
        _graphicsPipeline = graphicsPipeline;
        _triangleVertexBuffer = triangleVertexBuffer;
    }

    public void Render(BasicRenderContext renderContext)
    {
        SwapchainTexture swapchainTexture = renderContext.SwapchainTexture;

        // A resolve texture has the size and format of its multisampled target, so the targets follow the window.
        if (_colorTarget == null || _colorTarget.Size != swapchainTexture.Size || _colorTarget.Format != swapchainTexture.Format)
        {
            _colorTarget?.Dispose();
            _depthBuffer?.Dispose();
            _colorTarget = _gpuDevice.CreateColorTargetTexture(swapchainTexture.Size, swapchainTexture.Format, Samples);
            _depthBuffer = _gpuDevice.CreateDepthBufferTexture(swapchainTexture.Size, DepthFormat, sampleCount: Samples);
        }

        renderContext.CommandBuffer.PushFragmentUniformData(0, FColors.Magenta);
        using RenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(_colorTarget, swapchainTexture, _resolveSettings)
            .SetDepthBuffer(_depthBuffer!, _depthSettings)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_triangleVertexBuffer);
        renderPass.DrawPrimitive();
    }

    public static MultisamplingRenderer Create(GpuDevice gpuDevice, GraphicsPipelineBuilder graphicsPipelineBuilder, GpuMemorySystem gpuMemorySystem)
    {
        // A thin, tilted triangle, whose long edges show the stair steps that multisampling smooths.
        PositionVertex[] triangle =
        [
            new Vector3(-0.8f, -0.6f, 0.5f),
            new Vector3(0.1f, 0.8f, 0.5f),
            new Vector3(0.8f, -0.2f, 0.5f)
        ];
        GpuVertexBuffer<PositionVertex> triangleVertexBuffer = gpuMemorySystem.CreateVertexBuffer(triangle);

        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .EnableDepthTesting(DepthFormat)
            .EnableMultiSampling(Samples)
            .SetCullMode(CullMode.None)
            .Build();

        return new MultisamplingRenderer(gpuDevice, graphicsPipeline, triangleVertexBuffer);
    }
}
