using System.Numerics;
using Pixely.Content;
using Pixely.Gpu;
using Pixely.RenderOrchestration;
using Pixely.Shaders;

namespace Pixely.Ui;

/// <summary>
/// Paints a built <see cref="IUiPaintSource"/> into a persistent texture the size of its raster and
/// blits that texture over the frame at the source's present scale. The texture is only repainted
/// when the instructions changed, so a static UI costs one quad per frame. A pixelated root's raster
/// is its viewport, and nearest sampling in the blit is what makes an integer scale pixel-exact; a
/// smooth root's raster is the target, blitted one to one. The build itself belongs to
/// <see cref="UiUpdateSystem"/>, which is why this holds a source rather than the root. Where the
/// blit lands is the selector's answer for the frame's context, by default its colour target.
/// </summary>
internal sealed class UiRenderer<TRenderContext> : IRenderer<TRenderContext>, IDisposable
    where TRenderContext : IRenderContext
{
    private static readonly ColorTargetSettings _uiColorTargetSettings = new()
    {
        ClearColorValue = LinearColors.Transparent
    };

    private static readonly Matrix4x4 _presentViewProjection =
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, 1, 1, 0, 0, 1);

    private readonly GpuVertexBuffer<PositionTextureVertex> _vertexBuffer;
    private readonly GraphicsPipeline _quadPipeline;
    private readonly GraphicsPipeline _presentPipeline;
    private readonly Sampler _nearestSampler;
    private readonly Sampler _linearSampler;
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
            gpuDevice.CreateSampler(SamplerConfig.Linear),
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
        _nearestSampler = resources.NearestSampler;
        _linearSampler = resources.LinearSampler;
        _whiteTexture = resources.WhiteTexture;
        _colorTargetFormat = resources.ColorTargetFormat;
    }

    /// <summary>The GPU objects <see cref="Create"/> builds, handed to the constructor to assign.</summary>
    private readonly record struct GpuResources(
        GpuVertexBuffer<PositionTextureVertex> VertexBuffer,
        GraphicsPipeline QuadPipeline,
        GraphicsPipeline PresentPipeline,
        Sampler NearestSampler,
        Sampler LinearSampler,
        Texture WhiteTexture,
        TextureFormat ColorTargetFormat);

    public void Render(TRenderContext renderContext)
    {
        Texture colorTarget = _selectColorTarget(renderContext);
        ShortSize targetSize = colorTarget.Size;
        Vector2Int target = new(targetSize.Width, targetSize.Height);
        Vector2Int raster = _source.PaintedRasterSize;

        if (IsStale(_source.PaintedTargetSize, _source.TargetSize, target) || raster.X <= 0 || raster.Y <= 0)
        {
            // The retained texture is left alone: the catch-up build decides its size. It may paint
            // the same quads and leave PaintVersion where it is, so the repaint is asked for here.
            _retainedTextureDirty = true;

            if (_clearTarget)
            {
                ClearTarget(renderContext.CommandBuffer, colorTarget);
            }

            return;
        }

        Texture retainedTexture = EnsureRetainedTexture(new ShortSize((ushort)raster.X, (ushort)raster.Y));

        if (NeedsRepaint(_source.PaintVersion, _paintedVersion, _retainedTextureDirty))
        {
            Paint(renderContext.CommandBuffer, retainedTexture);
            _paintedVersion = _source.PaintVersion;
            _retainedTextureDirty = false;
        }

        Present(renderContext.CommandBuffer, colorTarget, retainedTexture, CreatePresentWorld(raster, _source.PaintedPresentScale, target));
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
    /// Where the retained texture lands on the target: its pixel 0 on target pixel 0, and each of
    /// its pixels <paramref name="scale"/> target pixels wide. A pixelated viewport is rounded up to
    /// cover the target, so the quad can overhang it by less than one logical pixel, which the
    /// target clips.
    /// </summary>
    internal static Matrix4x4 CreatePresentWorld(Vector2Int raster, float scale, Vector2Int target) =>
        Matrix4x4.CreateScale((float)(raster.X * (double)scale / target.X), (float)(raster.Y * (double)scale / target.Y), 1f);

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

    private void Paint(CommandBuffer commandBuffer, Texture retainedTexture)
    {
        IReadOnlyList<PaintInstruction> instructions = _source.Instructions;
        IReadOnlyList<PaintBatch> batches = _source.Batches;

        if (instructions.Count == 0)
        {
            Clear(commandBuffer, retainedTexture);
            return;
        }

        using RenderPass renderPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(retainedTexture, _uiColorTargetSettings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _viewProjection);

        // One pipeline for the whole UI; only the sampler, the sampling and the scissor change
        // between batches.
        renderPass.BindGraphicsPipeline(_quadPipeline);
        renderPass.BindVertexBuffer(_vertexBuffer);

        for (int batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            PaintBatch batch = batches[batchIndex];
            bool linear = batch.Filter == TextureFilter.Linear;
            renderPass.SetScissor(batch.Clip);
            renderPass.BindFragmentSampler(batch.Texture ?? _whiteTexture, linear ? _linearSampler : _nearestSampler);
            commandBuffer.PushFragmentUniformData(2, CreateSampling(linear));

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

    /// <summary>
    /// What the quad shader reads in its sampling slot: x is 1 to average every texel a pixel covers,
    /// which is what stands in for mipmaps when a texture is shown smaller than it is, and 0 to take
    /// the one sample under the pixel's centre.
    /// </summary>
    internal static Vector4 CreateSampling(bool linear) => new(linear ? 1f : 0f, 0f, 0f, 0f);

    private static void Clear(CommandBuffer commandBuffer, Texture retainedTexture)
    {
        using RenderPass clearPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(retainedTexture, _uiColorTargetSettings)
            .Build();
    }

    /// <summary>
    /// What a frame with nothing to present still owes the target when this renderer is the one
    /// that clears it: whatever is drawn after it expects a cleared target, stale build or not.
    /// </summary>
    private static void ClearTarget(CommandBuffer commandBuffer, Texture target)
    {
        using RenderPass clearPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(target, ColorTargetSettings.Clear)
            .Build();
    }

    private void Present(CommandBuffer commandBuffer, Texture target, Texture retainedTexture, Matrix4x4 world)
    {
        ColorTargetSettings settings = _clearTarget ? ColorTargetSettings.Clear : ColorTargetSettings.Load;

        using RenderPass presentPass = new RenderPassBuilder(commandBuffer)
            .AddColorTarget(target, settings)
            .Build();

        commandBuffer.PushVertexUniformData(0, _presentViewProjection);
        commandBuffer.PushVertexUniformData(1, world);

        presentPass.BindGraphicsPipeline(_presentPipeline);
        presentPass.BindVertexBuffer(_vertexBuffer);
        presentPass.BindFragmentSampler(retainedTexture, _nearestSampler);
        presentPass.DrawPrimitive();
    }

    private static Matrix4x4 CreateViewProjection(ShortSize size) =>
        Matrix4x4.CreateOrthographicOffCenterLeftHanded(0, size.Width, size.Height, 0, 0, 1);

    public void Dispose()
    {
        _retainedTexture?.Dispose();
    }
}
