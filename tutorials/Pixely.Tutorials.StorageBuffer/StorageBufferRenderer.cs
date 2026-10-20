using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Tutorials.StorageBuffer;

public class StorageBufferRenderer : IRenderer<BasicRenderContext>
{
    private readonly GraphicsPipeline _graphicsPipeline;
    private readonly GpuVertexBuffer<PositionVertex> _quadVertexBuffer;
    private readonly GpuStorageBuffer<LinearColor> _colorBuffer;
    private readonly int _colorCount;
    private float _time;

    public StorageBufferRenderer(
        GraphicsPipeline graphicsPipeline,
        GpuVertexBuffer<PositionVertex> quadVertexBuffer,
        GpuStorageBuffer<LinearColor> colorBuffer,
        int colorCount)
    {
        _graphicsPipeline = graphicsPipeline;
        _quadVertexBuffer = quadVertexBuffer;
        _colorBuffer = colorBuffer;
        _colorCount = colorCount;
    }

    public void Render(BasicRenderContext renderContext)
    {
        _time += 0.016f; // Approximate 60fps timestep

        // Cycle through colors every 0.5 seconds
        int colorIndex = (int)(_time * 2) % _colorCount;

        // Pass the index to the shader via uniform
        renderContext.CommandBuffer.PushFragmentUniformData(0, colorIndex);

        using RenderPass renderPass = new RenderPassBuilder(renderContext.CommandBuffer)
            .AddColorTarget(renderContext.SwapchainTexture)
            .SetSharedColorTargetSettings(ColorTargetSettings.Clear)
            .Build();

        renderPass.BindGraphicsPipeline(_graphicsPipeline);
        renderPass.BindVertexBuffer(_quadVertexBuffer);
        renderPass.BindFragmentStorageBuffer(_colorBuffer);

        renderPass.DrawPrimitive();
    }

    public static StorageBufferRenderer Create(
        ShaderLoader shaderLoader,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        GpuDevice gpuDevice)
    {
        // Create an array of colors to store in the storage buffer
        // This demonstrates passing more data than uniform slots would allow
        LinearColor[] colors =
        [
            new SrgbColor(255, 0, 0, 255), // Red
            new SrgbColor(255, 128, 0, 255), // Orange
            new SrgbColor(255, 255, 0, 255), // Yellow
            new SrgbColor(0, 255, 0, 255), // Green
            new SrgbColor(0, 255, 255, 255), // Cyan
            new SrgbColor(0, 0, 255, 255), // Blue
            new SrgbColor(128, 0, 255, 255), // Purple
            new SrgbColor(255, 0, 255, 255), // Magenta
        ];

        // Create storage buffer from color array
        GpuStorageBuffer<LinearColor> colorBuffer = gpuMemorySystem.CreateStorageBuffer<LinearColor>(colors);

        // Create vertex buffer with a quad
        GpuVertexBuffer<PositionVertex> quadVertexBuffer =
            gpuMemorySystem.CreateVertexBuffer(PositionShapes.VerticalQuad);

        // Build graphics pipeline
        GraphicsPipeline graphicsPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfig<PositionVertex>()
            .SetShaderProgram("shaders/shader")
            .AddColorFormatFromDisplay()
            .Build();

        return new StorageBufferRenderer(graphicsPipeline, quadVertexBuffer, colorBuffer, colors.Length);
    }
}
