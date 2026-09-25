using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Ui;

/// <summary>
/// Paints a built <see cref="IUiPaintSource"/> into a persistent texture the size of its viewport and
/// blits that texture over the frame at the source's scale. The texture is only repainted when the
/// instructions changed, so a static UI costs one quad per frame; the scale costs nothing beyond the
/// blit, and nearest sampling is what makes an integer scale pixel-exact. The build itself belongs
/// to <see cref="UiUpdateSystem"/>, which is why this holds a source rather than the root. Where the
/// blit lands is the selector's answer for the frame's context, by default its colour target.
/// </summary>
internal sealed class UiRenderer<TRenderContext> : IRenderer<TRenderContext>, IDisposable
    where TRenderContext : IRenderContext, allows ref struct
{
    private static readonly ColorTargetSettings _uiColorTargetSettings = new()
    {
        ClearColorValue = FColors.Transparent
    };

    private static readonly ColorTargetSettings _loadColorTargetSettings = new() { LoadOperation = LoadOperation.Load };

    private static readonly Matrix4x4 _presentViewProjection =
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, 1, 1, 0, 0, 1);

    private readonly GpuVertexBuffer<PositionTextureVertex> _vertexBuffer;
    private readonly GraphicsPipeline _quadPipeline;
    private readonly GraphicsPipeline _presentPipeline;
    private readonly Sampler _sampler;
    private readonly GpuDevice _gpuDevice;
    private readonly TextureFormat _colorTargetFormat;
    private readonly IUiPaintSource _source;
    private readonly Func<TRenderContext, Texture> _selectColorTarget;
    private readonly bool _clearTarget;

    // Solid fills sample this, which is what keeps colours and sprites on one pipeline.
    private readonly Texture _whiteTexture;

    // Created at the first frame that has a build to paint, and sized to that build's viewport.
    private Texture? _retainedTexture;
    private Matrix4x4 _viewProjection;
    private bool _retainedTextureDirty = true;
    private ulong _paintedVersion;

    public int RenderOrder { get; }
    public ViewScope ViewScope { get; }

    /// <summary>
    /// Builds the pipelines, sampler and textures the renderer needs. Kept out of the constructor
    /// so that constructing a renderer is assignment only.
    /// </summary>
    internal static UiRenderer<TRenderContext> Create(
        IUiPaintSource source,
        ViewScope viewScope,
        int renderOrder,
        bool clearTarget,
        Func<TRenderContext, Texture> selectColorTarget,
        GraphicsPipelineBuilder graphicsPipelineBuilder,
        GpuMemorySystem gpuMemorySystem,
        ShaderLoader shaderLoader,
        GpuDevice gpuDevice,
        Window window)
    {
        ReadOnlySpan<PositionTextureVertex> quad =
        [
            new(new Vector3(0.0f, 0.0f, 0.0f), new Vector2(0, 0)),
            new(new Vector3(1.0f, 0.0f, 0.0f), new Vector2(1, 0)),
            new(new Vector3(0.0f, 1.0f, 0.0f), new Vector2(0, 1)),
            new(new Vector3(1.0f, 1.0f, 0.0f), new Vector2(1, 1)),
        ];

        GpuVertexBuffer<PositionTextureVertex> vertexBuffer = gpuMemorySystem.CreateVertexBuffer(quad);

        GraphicsShaderProgram quadShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/ui_quad");
        GraphicsShaderProgram presentShaderProgram = shaderLoader.LoadGraphicsShaderProgram("shaders/ui_present");

        TextureFormat colorTargetFormat = window.ColorTargetFormat;

        // No depth attachment: submission order is paint order, which clipping needs anyway.
        GraphicsPipeline quadPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(vertexBuffer)
            .SetShaderProgram(quadShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.PremultipliedAlpha)
            .SetCullMode(CullMode.None)
            .Build();

        // The retained texture holds premultiplied colour, so it is blended as such rather than
        // with straight alpha.
        GraphicsPipeline presentPipeline = graphicsPipelineBuilder
            .SetPrimitiveType(PrimitiveType.TriangleStrip)
            .AddVertexBufferConfigBasedOnBuffer(vertexBuffer)
            .SetShaderProgram(presentShaderProgram)
            .AddColorTarget(colorTargetFormat, BlendingState.PremultipliedAlpha)
            .SetCullMode(CullMode.None)
            .Build();

        using RawImage whitePixel = new([255, 255, 255, 255], new ShortSize(1, 1), PixelFormat.Abgr8888);

        GpuResources resources = new(
            vertexBuffer,
            quadPipeline,
            presentPipeline,
            gpuDevice.CreateSampler(SamplerConfig.PixelArt),
            gpuMemorySystem.CreateTexture(whitePixel),
            colorTargetFormat);

        return new UiRenderer<TRenderContext>(source, viewScope, renderOrder, clearTarget, selectColorTarget, gpuDevice, resources);
    }

    private UiRenderer(
        IUiPaintSource source,
        ViewScope viewScope,
        int renderOrder,
        bool clearTarget,
        Func<TRenderContext, Texture> selectColorTarget,
        GpuDevice gpuDevice,
        GpuResources resources)
    {
        _source = source;
        ViewScope = viewScope;
        RenderOrder = renderOrder;
        _clearTarget = clearTarget;
        _selectColorTarget = selectColorTarget;
        _gpuDevice = gpuDevice;

        _vertexBuffer = resources.VertexBuffer;
        _quadPipeline = resources.QuadPipeline;
        _presentPipeline = resources.PresentPipeline;
        _sampler = resources.Sampler;
        _whiteTexture = resources.WhiteTexture;
        _colorTargetFormat = resources.ColorTargetFormat;
    }

    /// <summary>The GPU objects <see cref="Create"/> builds, handed to the constructor to assign.</summary>
    private readonly record struct GpuResources(
        GpuVertexBuffer<PositionTextureVertex> VertexBuffer,
        GraphicsPipeline QuadPipeline,
        GraphicsPipeline PresentPipeline,
        Sampler Sampler,
        Texture WhiteTexture,
        TextureFormat ColorTargetFormat);

    public void Render(ref TRenderContext renderContext)
    {
        Texture colorTarget = _selectColorTarget(renderContext);
        ref CommandBuffer commandBuffer = ref renderContext.CommandBuffer;
        ShortSize targetSize = colorTarget.Size;
        Vector2Int target = new(targetSize.Width, targetSize.Height);
        Vector2Int viewport = _source.PaintedViewportSize;

        if (IsStale(_source.PaintedTargetSize, _source.TargetSize, target) || viewport.X <= 0 || viewport.Y <= 0)
        {
            // The retained texture is left alone: the catch-up build decides its size. It may paint
            // the same quads and leave PaintVersion where it is, so the repaint is asked for here.
            _retainedTextureDirty = true;

            if (_clearTarget)
            {
                ClearTarget(ref commandBuffer, colorTarget);
            }

            return;
        }

        Texture retainedTexture = EnsureRetainedTexture(new ShortSize((ushort)viewport.X, (ushort)viewport.Y));

        if (NeedsRepaint(_source.PaintVersion, _paintedVersion, _retainedTextureDirty))
        {
            Paint(ref commandBuffer, retainedTexture);
            _paintedVersion = _source.PaintVersion;
            _retainedTextureDirty = false;
        }

        Present(ref commandBuffer, colorTarget, retainedTexture, CreatePresentWorld(viewport, _source.PaintedScale, target));
    }

    /// <summary>
    /// Whether the completed instructions describe geometry this frame cannot draw. Two questions,
    /// and either one is enough. Did the build finish at the target it was asked for — a callback
    /// can move the target after layout ran. And is that target still the one being drawn into — a
    /// resize landing between the update phase and here breaks it. Either way nothing is presented
    /// until a matching build lands, because stretching the previous frame's geometry into another
    /// target is worse than a blank one.
    /// </summary>
    internal static bool IsStale(Vector2Int paintedTargetSize, Vector2Int targetSize, Vector2Int target)
    {
        return paintedTargetSize != targetSize || paintedTargetSize != target;
    }

    /// <summary>
    /// Where the retained texture lands on the target: logical pixel 0 on target pixel 0, and each
    /// logical pixel <paramref name="scale"/> target pixels wide. The viewport is rounded up to cover
    /// the target, so the quad can overhang it by less than one logical pixel, which the target clips.
    /// </summary>
    internal static Matrix4x4 CreatePresentWorld(Vector2Int viewport, float scale, Vector2Int target) =>
        Matrix4x4.CreateScale((float)(viewport.X * (double)scale / target.X), (float)(viewport.Y * (double)scale / target.Y), 1f);

    /// <summary>
    /// Whether the retained texture no longer shows what the source holds. Compared against the
    /// paint version this renderer last painted rather than against whether a build just happened,
    /// so a renderer that missed one still repaints instead of depending on having been its caller.
    /// </summary>
    internal static bool NeedsRepaint(ulong paintVersion, ulong paintedVersion, bool retainedTextureDirty)
    {
        return paintVersion != paintedVersion || retainedTextureDirty;
    }

    private Texture EnsureRetainedTexture(ShortSize size)
    {
        if (_retainedTexture != null && _retainedTexture.Size == size)
        {
            return _retainedTexture;
        }

        _retainedTexture?.Dispose();
        _retainedTexture = _gpuDevice.CreateColorTargetTexture(size, _colorTargetFormat);
        _viewProjection = CreateViewProjection(size);
        _retainedTextureDirty = true;
        return _retainedTexture;
    }

    private void Paint(ref CommandBuffer commandBuffer, Texture retainedTexture)
    {
        IReadOnlyList<PaintInstruction> instructions = _source.Instructions;
        IReadOnlyList<PaintBatch> batches = _source.Batches;

        if (instructions.Count == 0)
        {
            Clear(ref commandBuffer, retainedTexture);
            return;
        }

        using RenderPass renderPass = new RenderPassBuilder(ref commandBuffer)
            .AddColorTarget(retainedTexture, _uiColorTargetSettings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _viewProjection);

        // One pipeline for the whole UI; only the sampler and the scissor change between batches.
        renderPass.BindGraphicsPipeline(_quadPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            PaintBatch batch = batches[batchIndex];
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
    }

    private static void Clear(ref CommandBuffer commandBuffer, Texture retainedTexture)
    {
        using RenderPass clearPass = new RenderPassBuilder(ref commandBuffer)
            .AddColorTarget(retainedTexture, _uiColorTargetSettings)
            .Build();
    }

    /// <summary>
    /// What a frame with nothing to present still owes the target when this renderer is the one
    /// that clears it: whatever is drawn after it expects a cleared target, stale build or not.
    /// </summary>
    private static void ClearTarget(ref CommandBuffer commandBuffer, Texture target)
    {
        using RenderPass clearPass = new RenderPassBuilder(ref commandBuffer)
            .AddColorTarget(target, ColorTargetSettings.Clear)
            .Build();
    }

    private void Present(ref CommandBuffer commandBuffer, Texture target, Texture retainedTexture, Matrix4x4 world)
    {
        ColorTargetSettings settings = _clearTarget ? ColorTargetSettings.Clear : _loadColorTargetSettings;

        using RenderPass presentPass = new RenderPassBuilder(ref commandBuffer)
            .AddColorTarget(target, settings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _presentViewProjection);
        commandBuffer.PushVertexUniformData(1, world);

        presentPass.BindGraphicsPipeline(_presentPipeline);
        presentPass.BindVertexBuffer(_vertexBuffer);
        presentPass.BindFragmentSampler(retainedTexture, _sampler);
        presentPass.DrawPrimitive();
    }

    private static Matrix4x4 CreateViewProjection(ShortSize size) =>
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, size.Width, size.Height, 0, 0, 1);

    public void Dispose()
    {
        _retainedTexture?.Dispose();
    }
}
