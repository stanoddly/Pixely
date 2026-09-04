using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Ui;

/// <summary>
/// Paints a <see cref="UiRoot"/> into a persistent texture and blits that texture over the frame.
/// The texture is only repainted when the tree changed, so a static UI costs one quad per frame.
/// </summary>
internal sealed class UiRenderer<TRenderContext> : IRenderer<TRenderContext>, IDisposable
    where TRenderContext : IRenderContext
{
    private static readonly ColorTargetSettings _uiColorTargetSettings = new()
    {
        ClearColorValue = FColors.Transparent
    };

    private static readonly Matrix4x4 _presentViewProjection =
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, 1, 1, 0, 0, 1);

    private readonly GpuVertexBuffer<PositionTextureVertex> _vertexBuffer;
    private readonly GraphicsPipeline _quadPipeline;
    private readonly GraphicsPipeline _presentPipeline;
    private readonly Sampler _sampler;
    private readonly GpuDevice _gpuDevice;
    private readonly TextureFormat _colorTargetFormat;
    private readonly UiRoot _root;
    private readonly bool _clearTarget;

    // Solid fills sample this, which is what keeps colours and sprites on one pipeline.
    private readonly Texture _whiteTexture;

    private Texture _retainedTexture;
    private Matrix4x4 _viewProjection;
    private bool _retainedTextureDirty = true;

    public int Order { get; }
    public ViewScope ViewScope { get; }

    internal UiRenderer(
        UiRoot root,
        ViewScope viewScope,
        int order,
        bool clearTarget,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        ShaderLoader shaderLoader,
        GpuDevice gpuDevice,
        Window window)
    {
        _root = root;
        ViewScope = viewScope;
        Order = order;
        _clearTarget = clearTarget;
        _gpuDevice = gpuDevice;

        ReadOnlySpan<PositionTextureVertex> quad =
        [
            new(new Vector3(0.0f, 0.0f, 0.0f), new Vector2(0, 0)),
            new(new Vector3(1.0f, 0.0f, 0.0f), new Vector2(1, 0)),
            new(new Vector3(0.0f, 1.0f, 0.0f), new Vector2(0, 1)),
            new(new Vector3(1.0f, 1.0f, 0.0f), new Vector2(1, 1)),
        ];

        _vertexBuffer = gpuMemorySystem.CreateVertexBuffer(quad);

        GraphicsShaderProgram quadShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/ui_quad");
        GraphicsShaderProgram presentShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/ui_present");

        TextureFormat colorTargetFormat = window.ColorTargetFormat;
        ShortSize renderSize = window.RenderSizeInPixels;
        _colorTargetFormat = colorTargetFormat;

        // No depth attachment: submission order is paint order, which clipping needs anyway.
        _quadPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(_vertexBuffer)
            .SetShaderProgram(quadShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.PremultipliedAlpha)
            .SetCullMode(CullMode.None)
            .Build();

        // The retained texture holds premultiplied colour, so it is blended as such rather than
        // with straight alpha.
        _presentPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(_vertexBuffer)
            .SetShaderProgram(presentShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.PremultipliedAlpha)
            .SetCullMode(CullMode.None)
            .Build();

        _sampler = gpuDevice.CreateSampler(SamplerConfig.PixelArt);
        _retainedTexture = gpuDevice.CreateColorTargetTexture(renderSize, colorTargetFormat);
        _viewProjection = CreateViewProjection(renderSize);

        using RawImage whitePixel = new([255, 255, 255, 255], new ShortSize(1, 1), PixelFormat.Abgr8888);
        _whiteTexture = gpuMemorySystem.CreateTexture(whitePixel);
    }

    public void Render(TRenderContext renderContext)
    {
        ShortSize targetSize = renderContext.ColorTarget.Size;
        ResizeRetainedTextureIfNeeded(targetSize);

        _root.SetViewportSize(new Vector2Int(targetSize.Width, targetSize.Height));

        bool rebuilt = _root.Update();

        // Instructions laid out for a different viewport would draw the previous frame's geometry
        // at the new size, so the texture is cleared until a matching build lands.
        if (_root.PaintedViewportSize != new Vector2Int(targetSize.Width, targetSize.Height))
        {
            Clear(renderContext.CommandBuffer);
            Present(renderContext.CommandBuffer, renderContext.ColorTarget);
            return;
        }

        if (rebuilt || _retainedTextureDirty)
        {
            Paint(renderContext.CommandBuffer);
            _retainedTextureDirty = false;
        }

        Present(renderContext.CommandBuffer, renderContext.ColorTarget);
    }

    private void ResizeRetainedTextureIfNeeded(ShortSize newSize)
    {
        if (_retainedTexture.Size == newSize)
        {
            return;
        }

        _retainedTexture.Dispose();
        _retainedTexture = _gpuDevice.CreateColorTargetTexture(newSize, _colorTargetFormat);
        _viewProjection = CreateViewProjection(newSize);
        _retainedTextureDirty = true;
    }

    private void Paint(CommandBuffer commandBuffer)
    {
        IReadOnlyList<PaintInstruction> instructions = _root.Instructions;
        IReadOnlyList<PaintBatch> batches = _root.Batches;

        if (instructions.Count == 0)
        {
            Clear(commandBuffer);
            return;
        }

        using IRenderPass renderPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(_retainedTexture, _uiColorTargetSettings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _viewProjection);

        // One pipeline for the whole UI; only the sampler and the scissor change between batches.
        renderPass.BindGraphicsPipeline(_quadPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);

        foreach (PaintBatch batch in batches)
        {
            renderPass.SetScissor(batch.Clip);
            renderPass.BindFragmentSampler(batch.Texture ?? _whiteTexture, _sampler);

            for (int i = batch.Start; i < batch.Start + batch.Count; i++)
            {
                PaintInstruction instruction = instructions[i];

                Matrix4x4 world =
                    Matrix4x4.CreateScale(instruction.Area.Width, instruction.Area.Height, 1.0f) *
                    Matrix4x4.CreateTranslation(instruction.Area.X, instruction.Area.Y, 0.0f);

                commandBuffer.PushVertexUniformData(1, world);
                commandBuffer.PushFragmentUniformData(0, instruction.Uvs);
                commandBuffer.PushFragmentUniformData(1, instruction.Tint);

                renderPass.DrawPrimitive();
            }
        }

        renderPass.ClearScissor();
    }

    private void Clear(CommandBuffer commandBuffer)
    {
        using IRenderPass clearPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(_retainedTexture, _uiColorTargetSettings)
            .Build();
    }

    private void Present(CommandBuffer commandBuffer, Texture target)
    {
        ColorTargetSettings settings = _clearTarget
            ? ColorTargetSettings.Clear
            : new ColorTargetSettings { LoadOperation = LoadOperation.Load };

        using IRenderPass presentPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(target, settings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _presentViewProjection);
        commandBuffer.PushVertexUniformData(1, Matrix4x4.Identity);

        presentPass.BindGraphicsPipeline(_presentPipeline);
        presentPass.BindVertexBuffer(_vertexBuffer);
        presentPass.BindFragmentSampler(_retainedTexture, _sampler);
        presentPass.DrawPrimitive();
    }

    private static Matrix4x4 CreateViewProjection(ShortSize size) =>
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, size.Width, size.Height, 0, 0, 1);

    public void Dispose()
    {
        _retainedTexture.Dispose();
    }
}
